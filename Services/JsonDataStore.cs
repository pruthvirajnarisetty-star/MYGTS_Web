using System.Text.Json;
using System.Text.Json.Serialization;
using MygtsMvc.Models;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Services;

public interface IAppDataStore
{
    Task<AppData> SnapshotAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(Action<AppData> update, CancellationToken cancellationToken = default);
    Task UpdateDeviceAsync(string id, Action<DeviceRecord> update, CancellationToken cancellationToken = default);
    Task ReplaceAsync(AppData replacement, CancellationToken cancellationToken = default);
    string DataFilePath { get; }

    /// <summary>Look up a single active user by email without loading all tables.</summary>
    Task<UserRecord?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Look up a single user by ID without loading all tables.</summary>
    Task<UserRecord?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Update only ActiveSessionToken for one user without loading all tables.</summary>
    Task UpdateUserSessionTokenAsync(string userId, string sessionToken, CancellationToken cancellationToken = default);

    /// <summary>Check database connectivity and return status, message, and path.</summary>
    Task<(bool IsConnected, string Message, string FilePath)> CheckConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Targeted Recharges queries & updates touching only the Recharges table.</summary>
    Task<List<RechargeRecord>> GetRechargesAsync(CancellationToken cancellationToken = default);
    Task SaveRechargesAsync(List<RechargeFormViewModel> rows, CancellationToken cancellationToken = default);
    Task DeleteRechargeAsync(string id, CancellationToken cancellationToken = default);
    void InvalidateCache();
}

public sealed class JsonDataStore : IAppDataStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly string _filePath;
    private AppData? _cache;
    public void InvalidateCache() => _cache = null;

    public JsonDataStore(IHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration["Data:File"] ?? "Data/mygts.json";
        if (!Path.IsPathRooted(configured))
        {
            var contentRoot = environment.ContentRootPath;
            var dir = new DirectoryInfo(contentRoot);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "mygts_website.csproj")))
            {
                dir = dir.Parent;
            }
            if (dir != null) { contentRoot = dir.FullName; }
            _filePath = Path.Combine(contentRoot, configured);
        }
        else
        {
            _filePath = configured;
        }
    }

    public string DataFilePath => _filePath;

    public async Task<AppData> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return Clone(_cache!);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(Action<AppData> update, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            update(_cache!);
            Normalize(_cache!);
            await PersistAsync(_cache!, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReplaceAsync(AppData replacement, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(data =>
        {
            data.Users = replacement.Users ?? new();
            data.Devices = replacement.Devices ?? new();
            data.Transactions = replacement.Transactions ?? new();
            data.Invoices = replacement.Invoices ?? new();
            data.Config = replacement.Config ?? new();
            data.ManualReminders = replacement.ManualReminders ?? new();
        }, cancellationToken);
    }

    public async Task UpdateDeviceAsync(string id, Action<DeviceRecord> update, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(data =>
        {
            var device = data.Devices.FirstOrDefault(d => d.Id == id);
            if (device != null) update(device);
        }, cancellationToken);
    }

    // JsonDataStore keeps the whole file in memory, so these targeted lookups
    // just read from the in-memory cache via SnapshotAsync — still fast.
    public async Task<UserRecord?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var data = await SnapshotAsync(cancellationToken);
        return data.Users.FirstOrDefault(u => u.IsActive && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<UserRecord?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var data = await SnapshotAsync(cancellationToken);
        return data.Users.FirstOrDefault(u => u.Id == userId);
    }

    public async Task UpdateUserSessionTokenAsync(string userId, string sessionToken, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(data =>
        {
            var user = data.Users.FirstOrDefault(u => u.Id == userId);
            if (user != null) user.ActiveSessionToken = sessionToken;
        }, cancellationToken);
    }

    public async Task<List<RechargeRecord>> GetRechargesAsync(CancellationToken cancellationToken = default)
    {
        var data = await SnapshotAsync(cancellationToken);
        return data.Recharges.OrderByDescending(r => r.CreatedAt).ToList();
    }

    public async Task SaveRechargesAsync(List<RechargeFormViewModel> rows, CancellationToken cancellationToken = default)
    {
        if (rows == null || rows.Count == 0) return;
        await UpdateAsync(data =>
        {
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Id))
                {
                    data.Recharges.Add(new RechargeRecord
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
                    var record = data.Recharges.FirstOrDefault(r => r.Id == row.Id);
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
        }, cancellationToken);
    }

    public async Task DeleteRechargeAsync(string id, CancellationToken cancellationToken = default)
    {
        await UpdateAsync(data => data.Recharges.RemoveAll(r => r.Id == id), cancellationToken);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        if (File.Exists(_filePath))
        {
            await using var stream = File.OpenRead(_filePath);
            _cache = await JsonSerializer.DeserializeAsync<AppData>(stream, _jsonOptions, cancellationToken) ?? new AppData();
        }
        else
        {
            _cache = new AppData();
        }

        Normalize(_cache);
        if (_cache.Users.Count == 0)
        {
            _cache.Users.Add(new UserRecord
            {
                Email = "exec@mygts.com",
                DisplayName = "Executive",
                Role = "Executive",
                PasswordHash = PasswordService.HashPassword("Executive@123")
            });
            _cache.Users.Add(new UserRecord
            {
                Email = "team@mygts.com",
                DisplayName = "Team Member",
                Role = "Team",
                PasswordHash = PasswordService.HashPassword("Team@123")
            });
            await PersistAsync(_cache, cancellationToken);
        }
    }

    private async Task PersistAsync(AppData data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporary = _filePath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, data, _jsonOptions, cancellationToken);
        }
        File.Move(temporary, _filePath, true);
    }

    private static AppData Clone(AppData source)
        => JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(source, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new AppData();

    private static void Normalize(AppData data)
    {
        data.Users ??= [];
        data.Devices ??= [];
        data.Transactions ??= [];
        data.Invoices ??= [];
        data.Config ??= [];
        data.ManualReminders ??= [];
        data.Recharges ??= [];

        foreach (var device in data.Devices)
        {
            device.Id = string.IsNullOrWhiteSpace(device.Id) ? Guid.NewGuid().ToString("N") : device.Id;
            device.StockStatus = string.IsNullOrWhiteSpace(device.StockStatus) ? "Used" : device.StockStatus;
            device.LifecycleStatus = string.IsNullOrWhiteSpace(device.LifecycleStatus) ? device.Status : device.LifecycleStatus;
        }
        foreach (var transaction in data.Transactions)
        {
            transaction.Id = string.IsNullOrWhiteSpace(transaction.Id) ? Guid.NewGuid().ToString("N") : transaction.Id;
        }
        foreach (var invoice in data.Invoices)
        {
            invoice.Id = string.IsNullOrWhiteSpace(invoice.Id) ? Guid.NewGuid().ToString("N") : invoice.Id;
            if (invoice.Amount == 0 && invoice.Quantity > 0 && invoice.Rate > 0) invoice.Amount = invoice.Quantity * invoice.Rate;
            if (invoice.TotalWithTaxes == 0 && invoice.Amount > 0) invoice.TotalWithTaxes = invoice.Amount * 1.18m + invoice.CourierCharges;
            if (invoice.CostPerDevice == 0 && invoice.Quantity > 0) invoice.CostPerDevice = invoice.TotalWithTaxes / invoice.Quantity;
        }
    }

    public Task<(bool IsConnected, string Message, string FilePath)> CheckConnectionAsync(CancellationToken cancellationToken = default)
    {
        bool exists = File.Exists(_filePath);
        return Task.FromResult((exists, exists ? "JSON data store loaded." : $"JSON file not found at {_filePath}", _filePath));
    }
}
