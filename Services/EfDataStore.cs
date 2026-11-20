using Microsoft.EntityFrameworkCore;
using MygtsMvc.Models;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Services;

public sealed class EfDataStore : IAppDataStore
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (AppData Data, DateTime CachedAt)> _branchAppDataCaches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

    public void InvalidateCache()
    {
        _branchAppDataCaches.Clear();
        try
        {
            var ctx = _httpContextAccessor?.HttpContext;
            if (ctx != null)
            {
                ctx.Items.Remove("__AppDataSnapshot");
                ctx.Items.Remove("__AppDataSnapshot_DMG");
                ctx.Items.Remove("__AppDataSnapshot_RTO");
                ctx.Items.Remove("__AppDataSnapshot_Expenditure");
            }
        }
        catch { }
    }

    public EfDataStore(IServiceProvider serviceProvider, IHttpContextAccessor? httpContextAccessor = null)
    {
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public string DataFilePath => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyGts_database", "mygts_database.db"))
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyGts_database", "mygts_database.db")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyGts_database", "mygts_database");

    public async Task<AppData> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
        var currentBranch = context.CurrentBranch ?? "DMG";

        var httpContext = _httpContextAccessor?.HttpContext;
        var reqKey = $"__AppDataSnapshot_{currentBranch}";
        if (httpContext != null && httpContext.Items.TryGetValue(reqKey, out var cached) && cached is AppData cachedData)
        {
            return cachedData;
        }

        var now = DateTime.UtcNow;
        if (_branchAppDataCaches.TryGetValue(currentBranch, out var existing) && (now - existing.CachedAt) < CacheTtl)
        {
            if (httpContext != null)
            {
                httpContext.Items[reqKey] = existing.Data;
            }
            return existing.Data;
        }

        var appData = new AppData
        {
            Users = await context.Users.AsNoTracking().ToListAsync(cancellationToken),
            Devices = await context.Devices.AsNoTracking().ToListAsync(cancellationToken),
            Transactions = await context.Transactions.AsNoTracking().ToListAsync(cancellationToken),
            Invoices = await context.Invoices.AsNoTracking().ToListAsync(cancellationToken),
            Config = await context.Config.AsNoTracking().ToListAsync(cancellationToken),
            ManualReminders = await context.ManualReminders.AsNoTracking().ToListAsync(cancellationToken),
            Recharges = await context.Recharges.AsNoTracking().ToListAsync(cancellationToken),
            Vendors = await context.Vendors.AsNoTracking().ToListAsync(cancellationToken),
            CustomDropdownOptions = await context.CustomDropdownOptions.AsNoTracking().ToListAsync(cancellationToken),
            VendorCredits = await context.VendorCredits.AsNoTracking().ToListAsync(cancellationToken),
        };

        var allConfigs = await context.Config.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var dmgConfig = DeviceLifecycleHelper.BuildMergedConfig("DMG", allConfigs.Where(c => string.Equals(c.Branch, "DMG", StringComparison.OrdinalIgnoreCase)));
        var rtoConfig = DeviceLifecycleHelper.BuildMergedConfig("RTO", allConfigs.Where(c => string.Equals(c.Branch, "RTO", StringComparison.OrdinalIgnoreCase)));

        // Dynamically synchronize any processed recharges into devices and transactions across the whole site
        RechargeAuditHelper.SyncProcessedRecharges(appData);

        foreach (var d in appData.Devices)
        {
            var cfg = string.Equals(d.Branch, "RTO", StringComparison.OrdinalIgnoreCase) ? rtoConfig : dmgConfig;
            DeviceLifecycleHelper.SyncDevice(d, cfg);
        }

        _branchAppDataCaches[currentBranch] = (appData, now);

        if (httpContext != null)
        {
            httpContext.Items[reqKey] = appData;
        }

        return appData;
    }

    public async Task UpdateAsync(Action<AppData> update, CancellationToken cancellationToken = default)
    {
        InvalidateCache();

        _httpContextAccessor?.HttpContext?.Items.Remove("__AppDataSnapshot");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();

            // 1. Fetch EVERYTHING (tracked)
            var appData = new AppData
            {
                Users = await context.Users.ToListAsync(cancellationToken),
                Devices = await context.Devices.ToListAsync(cancellationToken),
                Transactions = await context.Transactions.ToListAsync(cancellationToken),
                Invoices = await context.Invoices.ToListAsync(cancellationToken),
                Config = await context.Config.ToListAsync(cancellationToken),
                ManualReminders = await context.ManualReminders.ToListAsync(cancellationToken),
                Recharges = await context.Recharges.ToListAsync(cancellationToken),
                Vendors = await context.Vendors.ToListAsync(cancellationToken),
                CustomDropdownOptions = await context.CustomDropdownOptions.ToListAsync(cancellationToken),
                VendorCredits = await context.VendorCredits.ToListAsync(cancellationToken),
            };

            // 2. Execute the user's lambda mutation
            update(appData);

            // Dynamically synchronize dates across Devices, Transactions, and Recharges before persisting
            RechargeAuditHelper.SyncProcessedRecharges(appData);

            // Data Integrity Guard across all branches: Ensure no device has null or empty DeviceName if linked to an invoice
            var invoiceTypeMap = appData.Invoices
                .Where(i => !string.IsNullOrWhiteSpace(i.InvoiceNo) && !string.IsNullOrWhiteSpace(i.DeviceType))
                .GroupBy(i => $"{i.InvoiceNo.Trim()}|{(i.Branch ?? "").Trim()}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().DeviceType.Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var d in appData.Devices)
            {
                if (string.IsNullOrWhiteSpace(d.DeviceName) && !string.IsNullOrWhiteSpace(d.InvoiceId))
                {
                    var branchKey = (d.Branch ?? "").Trim();
                    if (invoiceTypeMap.TryGetValue($"{d.InvoiceId.Trim()}|{branchKey}", out var matchedType) ||
                        invoiceTypeMap.TryGetValue($"{d.InvoiceId.Trim()}|", out matchedType))
                    {
                        d.DeviceName = matchedType;
                    }
                }
            }

            // 3. Diff for additions and deletions (updates are automatically tracked by EF Core)
            SyncList(context.Users, appData.Users);
            SyncList(context.Devices, appData.Devices);
            SyncList(context.Transactions, appData.Transactions);
            SyncList(context.Invoices, appData.Invoices);
            SyncList(context.Config, appData.Config);
            SyncList(context.ManualReminders, appData.ManualReminders);
            SyncList(context.Recharges, appData.Recharges);
            SyncList(context.Vendors, appData.Vendors);
            SyncList(context.VendorCredits, appData.VendorCredits);

            await context.SaveChangesAsync(cancellationToken);
            InvalidateCache();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReplaceAsync(AppData replacement, CancellationToken cancellationToken = default)
    {
        InvalidateCache();

        await UpdateAsync(data =>
        {
            data.Users = replacement.Users ?? new();
            data.Devices = replacement.Devices ?? new();
            data.Transactions = replacement.Transactions ?? new();
            data.Invoices = replacement.Invoices ?? new();
            data.Config = replacement.Config ?? new();
            data.ManualReminders = replacement.ManualReminders ?? new();
            data.Recharges = replacement.Recharges ?? new();
            data.Vendors = replacement.Vendors ?? new();
            data.VendorCredits = replacement.VendorCredits ?? new();
        }, cancellationToken);
    }

    private static void SyncList<T>(DbSet<T> dbSet, List<T> memoryList) where T : class
    {
        var localSet = new HashSet<T>(dbSet.Local, ReferenceEqualityComparer.Instance);
        var memorySet = new HashSet<T>(memoryList, ReferenceEqualityComparer.Instance);

        // Additions
        foreach (var item in memoryList)
        {
            if (!localSet.Contains(item))
            {
                dbSet.Add(item);
            }
        }

        // Deletions
        foreach (var item in localSet)
        {
            if (!memorySet.Contains(item))
            {
                dbSet.Remove(item);
            }
        }
    }

    /// <summary>
    /// Update a SINGLE known entity by ID — much faster than UpdateAsync which loads all tables.
    /// Use this whenever you are updating exactly one row (e.g. device status, transaction field).
    /// </summary>
    public async Task UpdateDeviceAsync(string id, Action<DeviceRecord> update, CancellationToken cancellationToken = default)
    {
        InvalidateCache();

        _httpContextAccessor?.HttpContext?.Items.Remove("__AppDataSnapshot");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            var entity = await context.Devices.FindAsync(new object[] { id }, cancellationToken);
            if (entity is null) return;
            update(entity);
            await context.SaveChangesAsync(cancellationToken);
            InvalidateCache();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Save a single new entity without loading all other tables.
    /// </summary>
    public async Task AddDeviceAsync(DeviceRecord device, CancellationToken cancellationToken = default)
    {
        InvalidateCache();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            context.Devices.Add(device);
            await context.SaveChangesAsync(cancellationToken);
            InvalidateCache();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Check database connectivity and return status, message, and path.
    /// </summary>
    public async Task<(bool IsConnected, string Message, string FilePath)> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                return (false, "Cannot connect to database file. Check file existence and folder permissions for IIS AppPool / IIS_IUSRS.", DataFilePath);
            }

            var userCount = await context.Users.AsNoTracking().CountAsync(cancellationToken);
            return (true, $"Database connected ({userCount} users registered)", DataFilePath);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, DataFilePath);
        }
    }

    /// <summary>
    /// Look up a single user by email without filtering IsActive — touches only the Users table.
    /// Use this during login to determine if user exists and whether they are active or inactive.
    /// </summary>
    public async Task<UserRecord?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
        var cleanEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(cleanEmail)) return null;

        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == cleanEmail || u.DisplayName.ToLower() == cleanEmail, cancellationToken);
    }

    /// <summary>
    /// Look up a single user by primary key — touches only the Users table.
    /// Use this in SessionValidationMiddleware instead of SnapshotAsync.
    /// </summary>
    public async Task<UserRecord?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    /// <summary>
    /// Update ActiveSessionToken for a single user — does NOT load any other table.
    /// Far faster than UpdateAsync which loads all 6 tables just to change one field.
    /// </summary>
    public async Task UpdateUserSessionTokenAsync(string userId, string sessionToken, CancellationToken cancellationToken = default)
    {
        InvalidateCache();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            var user = await context.Users.FindAsync(new object[] { userId }, cancellationToken);
            if (user != null)
            {
                user.ActiveSessionToken = sessionToken;
                await context.SaveChangesAsync(cancellationToken);
            InvalidateCache();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Fast targeted Recharges queries and mutations touching ONLY the Recharges table.
    /// Runs in 1-5ms instead of loading the entire database.
    /// </summary>
    public async Task<List<RechargeRecord>> GetRechargesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
        return await context.Recharges.AsNoTracking().OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task SaveRechargesAsync(List<RechargeFormViewModel> rows, CancellationToken cancellationToken = default)
    {
        InvalidateCache();

        if (rows == null || rows.Count == 0) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Id))
                {
                    context.Recharges.Add(new RechargeRecord
                    {
                        VehicleNo = (row.VehicleNo ?? "").Trim(),
                        Imei = (row.Imei ?? "").Trim(),
                        Iccid = (row.Iccid ?? "").Trim(),
                        SimAirtel = (row.SimAirtel ?? "").Trim(),
                        SimBsnl = (row.SimBsnl ?? "").Trim(),
                        Vendor = (row.Vendor ?? "").Trim(),
                        NetworkOperator = (row.NetworkOperator ?? "").Trim(),
                        PaymentStatus = string.IsNullOrWhiteSpace(row.PaymentStatus) ? "Not Paid" : row.PaymentStatus,
                        RechargeStatus = string.IsNullOrWhiteSpace(row.RechargeStatus) ? "Pending" : row.RechargeStatus,
                        RechargeDate = string.IsNullOrWhiteSpace(row.RechargeDate) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : row.RechargeDate,
                        ExpiryDate = (row.ExpiryDate ?? "").Trim(),
                        PreviousExpiryDate = (row.PreviousExpiryDate ?? "").Trim(),
                        ActivationDate = (row.ActivationDate ?? "").Trim(),
                        AmountPaid = 0,
                        Notes = (row.Notes ?? "").Trim(),
                        CreatedAt = DateTime.UtcNow.ToString("O")
                    });
                }
                else
                {
                    var record = await context.Recharges.FirstOrDefaultAsync(r => r.Id == row.Id, cancellationToken);
                    if (record != null)
                    {
                        record.VehicleNo = (row.VehicleNo ?? "").Trim();
                        record.Imei = (row.Imei ?? "").Trim();
                        record.Iccid = (row.Iccid ?? "").Trim();
                        record.SimAirtel = (row.SimAirtel ?? "").Trim();
                        record.SimBsnl = (row.SimBsnl ?? "").Trim();
                        record.Vendor = (row.Vendor ?? "").Trim();
                        record.NetworkOperator = (row.NetworkOperator ?? "").Trim();
                        record.PaymentStatus = string.IsNullOrWhiteSpace(row.PaymentStatus) ? "Not Paid" : row.PaymentStatus;
                        record.RechargeStatus = string.IsNullOrWhiteSpace(row.RechargeStatus) ? "Pending" : row.RechargeStatus;
                        record.RechargeDate = string.IsNullOrWhiteSpace(row.RechargeDate) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : row.RechargeDate;
                        record.ExpiryDate = (row.ExpiryDate ?? "").Trim();
                        record.PreviousExpiryDate = (row.PreviousExpiryDate ?? "").Trim();
                        record.ActivationDate = (row.ActivationDate ?? "").Trim();
                        record.AmountPaid = 0;
                        record.Notes = (row.Notes ?? "").Trim();
                    }
                }

            }

            // Strictly synchronize all processed recharges with matching devices and transactions
            var allRecharges = await context.Recharges.ToListAsync(cancellationToken);
            var allDevices = await context.Devices.ToListAsync(cancellationToken);
            var allTxns = await context.Transactions.ToListAsync(cancellationToken);
            var tempAppData = new AppData { Recharges = allRecharges, Devices = allDevices, Transactions = allTxns };
            RechargeAuditHelper.SyncProcessedRecharges(tempAppData);

            await context.SaveChangesAsync(cancellationToken);
            InvalidateCache();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteRechargeAsync(string id, CancellationToken cancellationToken = default)
    {
        InvalidateCache();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            var record = await context.Recharges.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (record != null)
            {
                context.Recharges.Remove(record);
                await context.SaveChangesAsync(cancellationToken);
            InvalidateCache();
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
