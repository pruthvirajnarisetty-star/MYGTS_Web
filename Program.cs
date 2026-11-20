using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Services;
using MygtsMvc.Models;
using MygtsMvc.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
using System.IO.Compression;

// Pin the entire application's default culture permanently to India (en-IN / ₹)
var indianCulture = new System.Globalization.CultureInfo("en-IN");
indianCulture.NumberFormat.CurrencySymbol = "₹";
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = indianCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = indianCulture;

var builder = WebApplication.CreateBuilder(args);

// Enforce Indian Rupee localization across all requests regardless of client headers or server OS locale
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { indianCulture };
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(indianCulture);
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.RequestCultureProviders.Clear();
});

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHostedService<DatabaseBackupService>();

// Enable gzip/brotli response compression for HTML, CSS, JS, JSON
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "text/html", "text/css", "text/javascript", "application/javascript",
        "application/json", "text/plain", "image/svg+xml"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// =========================================================================================
// [TITLE: DATABASE RESOLUTION & SAFE FIRST-TIME INITIALIZATION]
// The ConnectionStrings value in appsettings.json uses a relative placeholder path
// ("Data Source=Data/mygts_database.db") so no machine-specific absolute paths are
// baked into config files that get published.
//
// At runtime, the actual database is always resolved here to the shared ProgramData
// folder so all processes (development, IIS, background services) share one live DB:
//
// Previous implementation stored in user's %LocalAppData%, which caused issues in IIS:
// // var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGts_database");
// =========================================================================================
var dbFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyGts_database");
if (!Directory.Exists(dbFolder))
{
    Directory.CreateDirectory(dbFolder);
}

// Check for existing database file (handles both with and without .db extension)
var dbPath = File.Exists(Path.Combine(dbFolder, "mygts_database.db"))
    ? Path.Combine(dbFolder, "mygts_database.db")
    : (File.Exists(Path.Combine(dbFolder, "mygts_database"))
        ? Path.Combine(dbFolder, "mygts_database")
        : Path.Combine(dbFolder, "mygts_database.db"));

// If database does not exist in ProgramData yet (e.g. initial setup on a new machine),
// seed it once from bundled Data/mygts_database.db. Live data is NEVER overwritten.
if (!File.Exists(dbPath))
{
    var templateDb = Path.Combine(builder.Environment.ContentRootPath, "Data", "mygts_database.db");
    if (File.Exists(templateDb))
    {
        try { File.Copy(templateDb, dbPath, overwrite: false); } catch { /* Ignore if created concurrently */ }
    }
}

var connectionString = $"Data Source={dbPath};Cache=Shared;Pooling=True;";

// =========================================================================================
// [TITLE: ENTERPRISE DATA PROTECTION & PERSISTENT SESSION ARCHITECTURE]
// Persist cryptographic encryption keys to a permanent directory on disk so that user session
// tickets and cookies survive IIS worker process recycles, idle timeouts, and machine reboots.
// =========================================================================================
var keysFolder = Path.Combine(dbFolder, "keys");
if (!Directory.Exists(keysFolder))
{
    try { Directory.CreateDirectory(keysFolder); } catch { /* Ignore if created concurrently or permission error */ }
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("MYGTS_Website");

builder.Services.AddDbContext<MygtsMvc.Data.ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddSingleton<IAppDataStore, EfDataStore>();
builder.Services.AddSingleton<DashboardService>();
builder.Services.AddSingleton<SpreadsheetService>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<IExpenditureDataStore, ExpenditureDataStore>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "MYGTS_Auth";
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.IsEssential = true;
    });


builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ExecutiveOnly", policy => policy.RequireRole("Admin", "Executive"))
    .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));


var app = builder.Build();

// Response compression must come before static files and routing
app.UseResponseCompression();

// Prevent browser from caching protected HTML pages across tab/browser sessions
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (!path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }
    await next();
});

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

try
{
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "PRAGMA journal_mode=WAL;" +
                      "PRAGMA synchronous=NORMAL;" +
                      "PRAGMA mmap_size=268435456;" +
                      "PRAGMA cache_size=-64000;" +
                      "PRAGMA temp_store=MEMORY;" +
                      "CREATE INDEX IF NOT EXISTS IX_Devices_Branch ON Devices (Branch);" +
                      "CREATE INDEX IF NOT EXISTS IX_Devices_VehicleNo ON Devices (VehicleNo);" +
                      "CREATE INDEX IF NOT EXISTS IX_Devices_Branch_Vehicle ON Devices (Branch, VehicleNo);" +
                      "CREATE INDEX IF NOT EXISTS IX_Transactions_Branch ON Transactions (Branch);" +
                      "CREATE INDEX IF NOT EXISTS IX_Transactions_Date ON Transactions (Date);" +
                      "CREATE INDEX IF NOT EXISTS IX_Transactions_VehicleNo ON Transactions (VehicleNo);" +
                      "CREATE INDEX IF NOT EXISTS IX_Invoices_Branch ON Invoices (Branch);" +
                      "CREATE INDEX IF NOT EXISTS IX_Invoices_InvoiceDate ON Invoices (InvoiceDate);";
    try { cmd.ExecuteNonQuery(); } catch { }
    cmd.CommandText = "UPDATE Transactions SET Branch = 'DMG' WHERE Branch != 'DMG' AND Branch != 'RTO';" +
                      "UPDATE Devices SET Branch = 'DMG' WHERE Branch != 'DMG' AND Branch != 'RTO';" +
                      "UPDATE Invoices SET Branch = 'DMG' WHERE Branch != 'DMG' AND Branch != 'RTO';" +
                      "UPDATE Config SET Branch = 'DMG' WHERE Branch != 'DMG' AND Branch != 'RTO';" +
                      "UPDATE ManualReminders SET Branch = 'DMG' WHERE Branch != 'DMG' AND Branch != 'RTO';";
    cmd.ExecuteNonQuery();

    try
    {
        cmd.CommandText = "ALTER TABLE Users ADD COLUMN PermissionsJson TEXT NOT NULL DEFAULT '{}';";
        cmd.ExecuteNonQuery();
    }
    catch { /* Column already exists */ }

    try
    {
        cmd.CommandText = "ALTER TABLE Users ADD COLUMN WhatsAppPreference TEXT NOT NULL DEFAULT 'Ask';";
        cmd.ExecuteNonQuery();
    }
    catch { /* Column already exists */ }

    try
    {
        cmd.CommandText = "ALTER TABLE Devices ADD COLUMN DeviceSerialNo TEXT DEFAULT '';";
        cmd.ExecuteNonQuery();
    }
    catch { /* Column already exists */ }

    try
    {
        cmd.CommandText = "ALTER TABLE Devices ADD COLUMN Paid TEXT DEFAULT '';";
        cmd.ExecuteNonQuery();
    }
    catch { /* Column already exists */ }

    try
    {
        cmd.CommandText = "ALTER TABLE Devices ADD COLUMN IsMuted INTEGER NOT NULL DEFAULT 0;";
        try { cmd.ExecuteNonQuery(); } catch { }
        cmd.CommandText = "ALTER TABLE Devices ADD COLUMN AdminCost NUMERIC NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }
    catch { /* Column already exists */ }

    try
    {
        cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN Imei TEXT DEFAULT '';";
        cmd.ExecuteNonQuery();
    }
    catch { /* Column already exists */ }

    try
    {
        cmd.CommandText = "ALTER TABLE Transactions ADD COLUMN AdminCost NUMERIC NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }
    catch { /* Column already exists */ }

    try
    {
        cmd.CommandText = "UPDATE Devices SET StockStatus = 'Activated' WHERE (VehicleNo IS NULL OR trim(VehicleNo) = '') AND StockStatus = 'Used';";
        cmd.ExecuteNonQuery();
    }
    catch { }

    try
    {
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS Vendors (" +
                          "Id TEXT PRIMARY KEY, " +
                          "Name TEXT NOT NULL, " +
                          "Phone TEXT DEFAULT '', " +
                          "AltPhone TEXT DEFAULT '', " +
                          "Notes TEXT DEFAULT '', " +
                          "Branch TEXT NOT NULL DEFAULT 'DMG', " +
                          "CreatedAt TEXT DEFAULT '');" +
                          "CREATE INDEX IF NOT EXISTS IX_Vendors_Branch ON Vendors (Branch);" +
                          "CREATE INDEX IF NOT EXISTS IX_Vendors_Name ON Vendors (Name);";
        cmd.ExecuteNonQuery();
    }
    catch { /* Table already exists */ }

    try
    {
        cmd.CommandText = "ALTER TABLE Vendors ADD COLUMN BatchPrefix TEXT NOT NULL DEFAULT '';";
        try { cmd.ExecuteNonQuery(); } catch { }
        cmd.CommandText = "ALTER TABLE Vendors ADD COLUMN ExtraPhones TEXT NOT NULL DEFAULT '';";
        try { cmd.ExecuteNonQuery(); } catch { }
        cmd.CommandText = "ALTER TABLE Vendors ADD COLUMN WhatsAppNumber TEXT NOT NULL DEFAULT '';";
        try { cmd.ExecuteNonQuery(); } catch { }
        cmd.CommandText = "ALTER TABLE Vendors ADD COLUMN WhatsAppSameAsPhone INTEGER NOT NULL DEFAULT 1;";
        try { cmd.ExecuteNonQuery(); } catch { }
        cmd.CommandText = "ALTER TABLE Vendors ADD COLUMN IsOwner INTEGER NOT NULL DEFAULT 0;";
        try { cmd.ExecuteNonQuery(); } catch { }
    }
    catch { /* Columns already exist */ }

    try
    {
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS DataShareRecords (" +
                          "Id TEXT PRIMARY KEY, " +
                          "Branch TEXT NOT NULL DEFAULT 'DMG', " +
                          "VehicleNo TEXT DEFAULT '', " +
                          "Imei TEXT DEFAULT '', " +
                          "Iccid TEXT DEFAULT '', " +
                          "DeviceName TEXT DEFAULT '', " +
                          "DeviceBranch TEXT DEFAULT '', " +
                          "SharedBy TEXT DEFAULT '', " +
                          "SharedByEmail TEXT DEFAULT '', " +
                          "SharedByRole TEXT DEFAULT '', " +
                          "SharedAt TEXT DEFAULT '', " +
                          "Recipient TEXT DEFAULT '', " +
                          "Notes TEXT DEFAULT '');" +
                          "CREATE INDEX IF NOT EXISTS IX_DataShareRecords_VehicleNo ON DataShareRecords (VehicleNo);" +
                          "CREATE INDEX IF NOT EXISTS IX_DataShareRecords_Imei ON DataShareRecords (Imei);" +
                          "CREATE INDEX IF NOT EXISTS IX_DataShareRecords_SharedAt ON DataShareRecords (SharedAt);" +
                          "CREATE INDEX IF NOT EXISTS IX_DataShareRecords_Branch ON DataShareRecords (Branch);";
        cmd.ExecuteNonQuery();
    }
    catch { /* Table already exists */ }

    try
    {
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS CustomDropdownOptions (" +
                          "Id TEXT PRIMARY KEY, " +
                          "Category TEXT NOT NULL, " +
                          "Value TEXT NOT NULL, " +
                          "Branch TEXT NOT NULL DEFAULT 'DMG', " +
                          "CreatedAt TEXT DEFAULT '');" +
                          "CREATE INDEX IF NOT EXISTS IX_CustomDropdownOptions_Branch ON CustomDropdownOptions (Branch);" +
                          "CREATE INDEX IF NOT EXISTS IX_CustomDropdownOptions_Category ON CustomDropdownOptions (Category);";
        cmd.ExecuteNonQuery();
    }
    catch { /* Table already exists */ }
}
catch (Exception ex)
{
    Console.WriteLine("Startup migration failed: " + ex.Message);
}

// Run database migrations and seed initial data (creates Admin user if DB is empty)
try
{
    await MygtsMvc.Data.DataSeeder.SeedAsync(app.Services);
}
catch (Exception ex)
{
    Console.WriteLine("Seeder failed: " + ex.Message);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    if (app.Configuration.GetValue<bool>("Security:EnforceHttps", false))
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }
}

app.UseRequestLocalization();
app.UseStatusCodePagesWithReExecute("/Home/NotFound");

// Static files with long-lived cache headers for versioned assets
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        var name = ctx.File.Name;
        // Cache images, CSS, JS for 30 days (files use asp-append-version for cache busting)
        if (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "public, max-age=2592000, immutable";
        }
    }
});

// Seamless backward compatibility: redirect root module requests (/Devices, /Transactions, etc.) to the active branch Area
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.Trim('/') ?? "";
    var segments = path.Split('/');
    var firstSegment = segments.Length > 0 ? segments[0] : "";

    // Normalize user/users URL variations (including Area prefixes) directly to /Users
    if (string.Equals(firstSegment, "user", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(firstSegment, "users", StringComparison.OrdinalIgnoreCase))
    {
        var subPath = segments.Length > 1 ? string.Join('/', segments.Skip(1)) : "";
        var targetUrl = "/Users" + (string.IsNullOrEmpty(subPath) ? "" : "/" + subPath) + context.Request.QueryString;
        if (!string.Equals(context.Request.Path.Value, "/Users" + (string.IsNullOrEmpty(subPath) ? "" : "/" + subPath), StringComparison.Ordinal))
        {
            context.Response.Redirect(targetUrl, permanent: false, preserveMethod: true);
            return;
        }
    }
    else if ((string.Equals(firstSegment, "DMG", StringComparison.OrdinalIgnoreCase) || string.Equals(firstSegment, "RTO", StringComparison.OrdinalIgnoreCase)) &&
             segments.Length > 1 &&
             (string.Equals(segments[1], "Users", StringComparison.OrdinalIgnoreCase) || string.Equals(segments[1], "User", StringComparison.OrdinalIgnoreCase)))
    {
        var subPath = segments.Length > 2 ? string.Join('/', segments.Skip(2)) : "";
        var targetUrl = "/Users" + (string.IsNullOrEmpty(subPath) ? "" : "/" + subPath) + context.Request.QueryString;
        context.Response.Redirect(targetUrl, permanent: false);
        return;
    }

    // Direct alias: Expenditure Invoices -> Expenditure Expenses
    if (string.Equals(path, "Expenditure/Invoices", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("Expenditure/Invoices/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/Expenditure/Expenses" + context.Request.QueryString, permanent: false);
        return;
    }

    var branchModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Dashboard", "Devices", "Transactions", "Invoices", "Inventory",
        "Reminders", "Recharges", "CompanyRecharges", "Vendors", "Config", "Sensorise", "DropdownOptions"
    };

    if (branchModules.Contains(firstSegment))
    {
        // Allow SwitchBranch to proceed to root DashboardController
        if (!(string.Equals(firstSegment, "Dashboard", StringComparison.OrdinalIgnoreCase) &&
              segments.Length > 1 &&
              string.Equals(segments[1], "SwitchBranch", StringComparison.OrdinalIgnoreCase)))
        {
            context.Request.Cookies.TryGetValue("mygts_branch", out var b);
            var isRto = string.Equals(b, "RTO", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(context.Request.Query["branch"], "RTO", StringComparison.OrdinalIgnoreCase);
            var isExpenditure = string.Equals(b, "Expenditure", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(context.Request.Query["branch"], "Expenditure", StringComparison.OrdinalIgnoreCase);

            if (isExpenditure && (string.Equals(firstSegment, "Invoices", StringComparison.OrdinalIgnoreCase) || string.Equals(firstSegment, "Expenses", StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Redirect("/Expenditure/Expenses" + context.Request.QueryString, permanent: false);
                return;
            }

            var branch = "DMG";
            if (!string.Equals(firstSegment, "Sensorise", StringComparison.OrdinalIgnoreCase) && isRto)
            {
                branch = "RTO";
            }
            var targetUrl = "/" + branch + "/" + path + context.Request.QueryString;
            context.Response.Redirect(targetUrl, permanent: false);
            return;
        }
    }
    await next();
});

app.UseRouting();

// Ensure mygts_branch cookie matches the route area whenever entering an Area path
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments.Length > 0)
    {
        var first = segments[0];
        if (string.Equals(first, "DMG", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(first, "RTO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(first, "Expenditure", StringComparison.OrdinalIgnoreCase))
        {
            var targetBranch = string.Equals(first, "RTO", StringComparison.OrdinalIgnoreCase) ? "RTO" :
                               (string.Equals(first, "Expenditure", StringComparison.OrdinalIgnoreCase) ? "Expenditure" : "DMG");
            if (!context.Request.Cookies.TryGetValue("mygts_branch", out var currentBranchCookie) ||
                !string.Equals(currentBranchCookie, targetBranch, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Cookies.Append("mygts_branch", targetBranch, new CookieOptions
                {
                    Path = "/",
                    MaxAge = TimeSpan.FromDays(365),
                    SameSite = SameSiteMode.Lax,
                    HttpOnly = false,
                    IsEssential = true
                });
            }
        }
    }
    await next();
});

app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "controllerOnly",
    pattern: "{controller}",
    defaults: new { action = "Index" },
    constraints: new { controller = "Inventory|Devices|Transactions|Invoices|Reminders|Dashboard|Backups|Config|Users|Vendors|Recharges|CompanyRecharges|Sensorise" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();

// trigger watch reload
