using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using MygtsMvc.Models;



namespace MygtsMvc.Data;



public static class DataSeeder

{

    public static async Task SeedAsync(IServiceProvider serviceProvider)

    {

        using var scope = serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();



        // Ensure the schema exists (safe on an already-migrated DB)

        // Note: migrations were already applied via 'dotnet ef database update' or the startup block.

        // We use EnsureCreated only as a safety net for brand-new databases without migrations.

        await context.Database.EnsureCreatedAsync();



        // Ensure WAL mode is active for resilience

        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
        await context.Database.ExecuteSqlRawAsync("PRAGMA mmap_size=268435456;");
        await context.Database.ExecuteSqlRawAsync("PRAGMA cache_size=-64000;");
        await context.Database.ExecuteSqlRawAsync("PRAGMA temp_store=MEMORY;");



        // Ensure extra columns exist in SQLite database safely without throwing failed command errors

        await EnsureColumnExistsAsync(context, "Vendors", "BatchPrefix", "TEXT NULL DEFAULT ''");

        await EnsureColumnExistsAsync(context, "Vendors", "ExtraPhones", "TEXT NULL DEFAULT ''");

        await EnsureColumnExistsAsync(context, "Vendors", "WhatsAppNumber", "TEXT NULL DEFAULT ''");

        await EnsureColumnExistsAsync(context, "Vendors", "WhatsAppSameAsPhone", "INTEGER NOT NULL DEFAULT 1");

        await EnsureColumnExistsAsync(context, "Users", "WhatsAppPreference", "TEXT NULL DEFAULT 'Ask'");

        await EnsureColumnExistsAsync(context, "Devices", "Paid", "TEXT NULL DEFAULT ''");

        await EnsureColumnExistsAsync(context, "Devices", "IsMuted", "INTEGER NOT NULL DEFAULT 0");

        await EnsureColumnExistsAsync(context, "Devices", "AdminCost", "NUMERIC NOT NULL DEFAULT 0");

        await EnsureColumnExistsAsync(context, "Transactions", "Imei", "TEXT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(context, "Transactions", "NetworkOperator", "TEXT NULL DEFAULT ''");
        await EnsureColumnExistsAsync(context, "Transactions", "AdminCost", "NUMERIC NOT NULL DEFAULT 0");



        // Backfill Device AdminCost from existing activation transactions if not yet populated

        try

        {

            await context.Database.ExecuteSqlRawAsync(@"

                UPDATE Devices

                SET AdminCost = (

                    SELECT t.AdminCost FROM Transactions t

                    WHERE LOWER(REPLACE(t.VehicleNo, ' ', '')) = LOWER(REPLACE(Devices.VehicleNo, ' ', ''))

                      AND t.AdminCost > 0

                    LIMIT 1

                )

                WHERE (AdminCost IS NULL OR AdminCost = 0)

                  AND EXISTS (

                    SELECT 1 FROM Transactions t

                    WHERE LOWER(REPLACE(t.VehicleNo, ' ', '')) = LOWER(REPLACE(Devices.VehicleNo, ' ', ''))

                      AND t.AdminCost > 0

                  );

            ");

        }

        catch { }



        // Synchronize all devices with current date lifecycle status

        await SyncDeviceLifecyclesAsync(context);



        // Ensure Company Recharge tables exist

        await context.Database.ExecuteSqlRawAsync(@"

            CREATE TABLE IF NOT EXISTS ""CompanyRechargeBatches"" (

                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_CompanyRechargeBatches"" PRIMARY KEY,

                ""Branch"" TEXT NOT NULL,

                ""PoNumber"" TEXT NOT NULL,

                ""PayReferenceNo"" TEXT NOT NULL,

                ""Date"" TEXT NOT NULL,

                ""Provider"" TEXT NOT NULL,

                ""AdvanceAmount"" TEXT NOT NULL,

                ""CarriedForwardAmount"" TEXT NOT NULL DEFAULT '0',

                ""CarriedOutAmount"" TEXT NOT NULL DEFAULT '0',

                ""TransferredFromBatchId"" TEXT NULL,

                ""TransferredFromBatchPo"" TEXT NULL,

                ""TransferredToBatchId"" TEXT NULL,

                ""TransferredToBatchPo"" TEXT NULL,

                ""TotalAmount"" TEXT NOT NULL,

                ""RemainingBalance"" TEXT NOT NULL,

                ""TotalDevices"" INTEGER NOT NULL,

                ""RenewalCount"" INTEGER NOT NULL,

                ""ReactivationCount"" INTEGER NOT NULL,

                ""Notes"" TEXT NOT NULL,

                ""CreatedAt"" TEXT NOT NULL

            );



            CREATE TABLE IF NOT EXISTS ""CompanyRechargeItems"" (

                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_CompanyRechargeItems"" PRIMARY KEY,

                ""Branch"" TEXT NOT NULL,

                ""BatchId"" TEXT NOT NULL,

                ""SlNo"" INTEGER NOT NULL,

                ""VehicleNo"" TEXT NOT NULL,

                ""Imei"" TEXT NOT NULL,

                ""Iccid"" TEXT NOT NULL,

                ""RequestId"" TEXT NOT NULL,

                ""ActionType"" TEXT NOT NULL,

                ""PerDeviceAmount"" TEXT NOT NULL,

                ""CreatedAt"" TEXT NOT NULL

            );



            CREATE TABLE IF NOT EXISTS ""VendorCredits"" (

                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_VendorCredits"" PRIMARY KEY,

                ""Branch"" TEXT NOT NULL,

                ""Vendor"" TEXT NOT NULL,

                ""Amount"" NUMERIC NOT NULL,

                ""RemainingBalance"" NUMERIC NOT NULL,

                ""Date"" TEXT NOT NULL,

                ""PaymentMode"" TEXT NOT NULL,

                ""PaidTo"" TEXT NOT NULL,

                ""Utr"" TEXT NOT NULL,

                ""SourceTxnId"" TEXT NOT NULL,

                ""Status"" TEXT NOT NULL,

                ""Notes"" TEXT NOT NULL,

                ""CreatedAt"" TEXT NOT NULL,

                ""SettledAt"" TEXT NULL

            );

        ");



        // Seed default config for company recharge rates if not present

        foreach (var b in new[] { "DMG", "RTO" })

        {

            if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "COMPANY_RECHARGE_RATE" && c.Branch == b))

            {

                context.Config.Add(new ConfigItem { Key = "COMPANY_RECHARGE_RATE", Branch = b, Value = "1298" });

            }

            if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "COMPANY_REACTIVATION_RATE" && c.Branch == b))

            {

                context.Config.Add(new ConfigItem { Key = "COMPANY_REACTIVATION_RATE", Branch = b, Value = "1534" });

            }

            if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "BLUEMINE_RECHARGE_RATE" && c.Branch == b))

            {

                context.Config.Add(new ConfigItem { Key = "BLUEMINE_RECHARGE_RATE", Branch = b, Value = "1156.40" });

            }

            if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "BLUEMINE_REACTIVATION_RATE" && c.Branch == b))

            {

                context.Config.Add(new ConfigItem { Key = "BLUEMINE_REACTIVATION_RATE", Branch = b, Value = "1392.40" });

            }

            if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "SMT_RECHARGE_RATE" && c.Branch == b))

            {

                context.Config.Add(new ConfigItem { Key = "SMT_RECHARGE_RATE", Branch = b, Value = "1298" });

            }

            if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "SMT_REACTIVATION_RATE" && c.Branch == b))

            {

                context.Config.Add(new ConfigItem { Key = "SMT_REACTIVATION_RATE", Branch = b, Value = "1534" });

            }

            if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "SENSORISE_RECHARGE_RATE" && c.Branch == b))

            {

                context.Config.Add(new ConfigItem { Key = "SENSORISE_RECHARGE_RATE", Branch = b, Value = "1298" });

            }

            if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "SENSORISE_REACTIVATION_RATE" && c.Branch == b))

            {

                context.Config.Add(new ConfigItem { Key = "SENSORISE_REACTIVATION_RATE", Branch = b, Value = "1534" });

            }

            if (b == "RTO")
            {
                if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "ACTIVATION_EXPIRY_1_YEAR_MONTHS" && c.Branch == b))
                {
                    context.Config.Add(new ConfigItem { Key = "ACTIVATION_EXPIRY_1_YEAR_MONTHS", Branch = b, Value = "12" });
                }
                if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "ACTIVATION_EXPIRY_1_YEAR_DAYS" && c.Branch == b))
                {
                    context.Config.Add(new ConfigItem { Key = "ACTIVATION_EXPIRY_1_YEAR_DAYS", Branch = b, Value = "0" });
                }
                if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "ACTIVATION_EXPIRY_2_YEAR_MONTHS" && c.Branch == b))
                {
                    context.Config.Add(new ConfigItem { Key = "ACTIVATION_EXPIRY_2_YEAR_MONTHS", Branch = b, Value = "24" });
                }
                if (!await context.Config.IgnoreQueryFilters().AnyAsync(c => c.Key == "ACTIVATION_EXPIRY_2_YEAR_DAYS" && c.Branch == b))
                {
                    context.Config.Add(new ConfigItem { Key = "ACTIVATION_EXPIRY_2_YEAR_DAYS", Branch = b, Value = "0" });
                }
            }

        }

        await context.SaveChangesAsync();



        // If users already exist, only backfill missing plain passwords for legacy users

        if (await context.Users.IgnoreQueryFilters().AnyAsync())

        {

            bool changed = false;

            var execUser = await context.Users.IgnoreQueryFilters()

                .FirstOrDefaultAsync(u => u.Email == "exec@mygts.com");

            if (execUser != null && string.IsNullOrWhiteSpace(execUser.PlainPassword))

            {

                execUser.PlainPassword = "Executive@123";

                changed = true;

            }



            var teamUser = await context.Users.IgnoreQueryFilters()

                .FirstOrDefaultAsync(u => u.Email == "team@mygts.com");

            if (teamUser != null && string.IsNullOrWhiteSpace(teamUser.PlainPassword))

            {

                teamUser.PlainPassword = "Team@123";

                changed = true;

            }



            if (changed)

                await context.SaveChangesAsync();

            return;

        }



        // Fresh database — seed default Admin and Executive users

        var passwordService = scope.ServiceProvider.GetRequiredService<MygtsMvc.Services.PasswordService>();

        var allPerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

        {

            ["Dashboard"]    = "Edit",

            ["Devices"]      = "Edit",

            ["Inventory"]    = "Edit",

            ["Transactions"] = "Edit",

            ["Invoices"]     = "Edit",

            ["Reminders"]    = "Edit",

        };

        var permsJson = JsonSerializer.Serialize(allPerms);



        context.Users.Add(new UserRecord

        {

            Email           = "admin@mygts.com",

            DisplayName     = "Admin",

            Role            = "Admin",

            PasswordHash    = passwordService.Hash("Admin@123"),

            PlainPassword   = "Admin@123",

            IsActive        = true,

            PermissionsJson = permsJson

        });

        context.Users.Add(new UserRecord

        {

            Email           = "admin2@mygts.com",

            DisplayName     = "Admin Backup",

            Role            = "Admin",

            PasswordHash    = passwordService.Hash("Admin@123"),

            PlainPassword   = "Admin@123",

            IsActive        = true,

            PermissionsJson = permsJson

        });

        context.Users.Add(new UserRecord

        {

            Email           = "exec@mygts.com",

            DisplayName     = "Executive",

            Role            = "Executive",

            PasswordHash    = passwordService.Hash("Executive@123"),

            PlainPassword   = "Executive@123",

            IsActive        = true,

            PermissionsJson = permsJson

        });



        await context.SaveChangesAsync();

        Console.WriteLine("Seeder: created default Admin and Executive users.");

    }



    public static async Task SyncDeviceLifecyclesAsync(ApplicationDbContext context)

    {

        try

        {

            var allConfig = await context.Config.AsNoTracking().ToListAsync();

            var configDmg = allConfig.Where(c => c.Branch == "DMG").ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

            var configRto = allConfig.Where(c => c.Branch == "RTO").ToDictionary(c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);



            var devices = await context.Devices.IgnoreQueryFilters().ToListAsync();

            bool hasChanges = false;

            foreach (var d in devices)

            {

                var cfg = string.Equals(d.Branch, "RTO", StringComparison.OrdinalIgnoreCase) ? configRto : configDmg;

                var prevStatus = d.Status;

                var prevLifecycle = d.LifecycleStatus;

                var prevDays = d.DaysPastExpiry;



                DeviceLifecycleHelper.SyncDevice(d, cfg);



                if (d.Status != prevStatus || d.LifecycleStatus != prevLifecycle || d.DaysPastExpiry != prevDays)

                {

                    hasChanges = true;

                }

            }



            if (hasChanges)

            {

                await context.SaveChangesAsync();

                Console.WriteLine("DataSeeder: Synchronized device lifecycle statuses in database.");

            }

        }

        catch (Exception ex)

        {

            Console.WriteLine($"DataSeeder: Device lifecycle sync failed: {ex.Message}");

        }

    }



    private static async Task EnsureColumnExistsAsync(ApplicationDbContext context, string tableName, string columnName, string columnDef)

    {

        var conn = context.Database.GetDbConnection();

        var wasOpen = conn.State == System.Data.ConnectionState.Open;

        if (!wasOpen) await conn.OpenAsync();

        try

        {

            using var cmd = conn.CreateCommand();

            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = '{columnName}'";

            var result = await cmd.ExecuteScalarAsync();

            var exists = result != null && Convert.ToInt32(result) > 0;

            if (!exists)

            {

                cmd.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnDef};";

                await cmd.ExecuteNonQueryAsync();

            }

        }

        catch

        {

            // Ignore any race condition on concurrent migrations

        }

        finally

        {

            if (!wasOpen) await conn.CloseAsync();

        }

    }

}

