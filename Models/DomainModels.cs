using System.ComponentModel.DataAnnotations;

using System.ComponentModel.DataAnnotations.Schema;



namespace MygtsMvc.Models;



public interface IBranchEntity

{

    string Branch { get; set; }

}



public sealed class AppData

{

    public List<UserRecord> Users { get; set; } = [];

    public List<DeviceRecord> Devices { get; set; } = [];

    public List<TransactionRecord> Transactions { get; set; } = [];

    public List<InvoiceRecord> Invoices { get; set; } = [];

    public List<ConfigItem> Config { get; set; } = [];

    public List<ManualReminder> ManualReminders { get; set; } = [];

    public List<RechargeRecord> Recharges { get; set; } = [];

    public List<VendorRecord> Vendors { get; set; } = [];

    public List<CustomDropdownOption> CustomDropdownOptions { get; set; } = [];

    public List<VendorCreditRecord> VendorCredits { get; set; } = [];

}



public sealed class UserRecord

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Email { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Role { get; set; } = "Team"; // "Admin", "Executive", "Team", "User"

    public string PasswordHash { get; set; } = "";

    public string PlainPassword { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public string PermissionsJson { get; set; } = "{}";

    public string ActiveSessionToken { get; set; } = "";

    public string WhatsAppPreference { get; set; } = "Ask"; // "Ask", "Web", "App"

    public string AllowedBranches { get; set; } = "DMG,RTO,Expenditure";

    public bool HasBranchAccess(string branch)
    {
        if (string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(AllowedBranches)) return true;
        var branches = AllowedBranches.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return branches.Any(b => string.Equals(b, branch, StringComparison.OrdinalIgnoreCase));
    }

    public List<string> GetAllowedBranchesList()
    {
        if (string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase))
            return new List<string> { "DMG", "RTO", "Expenditure" };
        if (string.IsNullOrWhiteSpace(AllowedBranches))
            return new List<string> { "DMG", "RTO", "Expenditure" };
        return AllowedBranches.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }





    public string GetPermission(string module)

    {

        if (Role == "Admin" || Role == "Executive") return "Edit";

        if (string.IsNullOrWhiteSpace(PermissionsJson)) return "Read";

        try

        {

            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(PermissionsJson);

            if (dict != null && dict.TryGetValue(module, out var perm) && !string.IsNullOrWhiteSpace(perm))

            {

                if (Role == "User" && perm == "Edit") return "Read";

                return perm;

            }

        }

        catch { }

        return "Read";

    }



    public bool CanView(string module)

    {

        if (Role == "Admin" || Role == "Executive") return true;

        return GetPermission(module) != "None";

    }



    public bool CanEdit(string module)

    {

        if (Role == "Admin" || Role == "Executive") return true;

        if (Role == "User") return false;

        return GetPermission(module) == "Edit";

    }

}



public sealed class DeviceRecord : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string SlNo { get; set; } = "";

    

    [NotMapped]

    public string SlNoSortable

    {

        get

        {

            if (string.IsNullOrWhiteSpace(SlNo)) return "0_";

            var s = SlNo.Trim();

            var padded = System.Text.RegularExpressions.Regex.Replace(s, @"\d+", m => m.Value.PadLeft(10, '0'));

            return char.IsDigit(s[0]) ? $"1_{padded}" : $"0_{padded}";

        }

    }

    public string VehicleNo { get; set; } = "";

    public string Iccid { get; set; } = "";

    public string Imei { get; set; } = "";

    public string DeviceSerialNo { get; set; } = "";

    public string SimAirtel { get; set; } = "";

    public string SimBsnl { get; set; } = "";

    public string Status { get; set; } = "Active";

    public string Type { get; set; } = "";

    public string DeviceName { get; set; } = "";

    public string InstallType { get; set; } = "New";

    public string InvoiceId { get; set; } = "";

    public string Vendor { get; set; } = "";

    public string Owner { get; set; } = "";

    public string CellNo { get; set; } = "";

    public string RegistrationDate { get; set; } = "";

    public string ActivationDate { get; set; } = "";



    public string LatestExpiryDate { get; set; } = "";

    public string EngineNo { get; set; } = "";

    public string ChasisNo { get; set; } = "";

    public string VehicleMake { get; set; } = "";

    public string VehicleModel { get; set; } = "";

    public string Rto { get; set; } = "";

    public string Address { get; set; } = "";

    public string ProofOfAddress { get; set; } = "";

    public string DocumentNumber { get; set; } = "";

    public string Notes { get; set; } = "";

    public string PreviousImei { get; set; } = "";

    public string HardwareHistory { get; set; } = "";

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public int? DaysPastExpiry { get; set; }

    public string LifecycleStatus { get; set; } = "Active";

    public string StockStatus { get; set; } = "Used";

    public decimal UnitCost { get; set; }

    public decimal AdminCost { get; set; } = 0m;

    public string ReplacementSource { get; set; } = "None";

    public string? ReplacedDeviceId { get; set; }

    public string? ReplacedDeviceSnapshot { get; set; }

    public string Branch { get; set; } = "DMG";

    public bool ExcludeFromUnpaid { get; set; } = false;

    public bool IsMuted { get; set; } = false;



    [NotMapped]
    public bool IsNotInUse => IsMuted 
        || string.Equals(Status?.Trim(), "Not In Use", StringComparison.OrdinalIgnoreCase)
        || string.Equals(LifecycleStatus?.Trim(), "Not In Use", StringComparison.OrdinalIgnoreCase)
        || string.Equals(VehicleNo?.Trim(), "Not In Use", StringComparison.OrdinalIgnoreCase)
        || (VehicleNo != null && (
            VehicleNo.IndexOf("Not in use", StringComparison.OrdinalIgnoreCase) >= 0 ||
            VehicleNo.IndexOf("NOTINUSE", StringComparison.OrdinalIgnoreCase) >= 0
        ));

    public string NetworkOperator { get; set; } = "";

    public string CdacCertificate { get; set; } = "NO";

    public string VldtCertificate { get; set; } = "NO";

    public string InformedDate { get; set; } = "";

    public string? Paid { get; set; } = "";

}



public sealed class TransactionRecord : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string SlNo { get; set; } = "";

    

    [NotMapped]

    public string SlNoSortable

    {

        get

        {

            if (string.IsNullOrWhiteSpace(SlNo)) return "0_";

            var s = SlNo.Trim();

            var padded = System.Text.RegularExpressions.Regex.Replace(s, @"\d+", m => m.Value.PadLeft(10, '0'));

            return char.IsDigit(s[0]) ? $"1_{padded}" : $"0_{padded}";

        }

    }

    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    public string VehicleNo { get; set; } = "";

    public string? Imei { get; set; } = "";

    public string? NetworkOperator { get; set; } = "";

    public string Vendor { get; set; } = "";

    public string InvoiceId { get; set; } = "";

    public string PaymentMode { get; set; } = "UPI";

    public string PaymentFor { get; set; } = "None";

    public decimal? Amount { get; set; }



    public string? BatchId { get; set; } = "";

    public string Utr { get; set; } = "";

    public string PaidTo { get; set; } = "";

    public string Extras { get; set; } = "None";

    public string Type { get; set; } = "New";

    public string ActivationDate { get; set; } = "";

    [NotMapped]
    public string EffectiveDate
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ActivationDate))
            {
                var clean = ActivationDate.Trim();
                if (clean.StartsWith("0226")) clean = "2026" + clean.Substring(4);
                if (DateTime.TryParse(clean, out var actDt))
                {
                    return actDt.ToString("yyyy-MM-dd");
                }
            }
            if (!string.IsNullOrWhiteSpace(Date))
            {
                var clean = Date.Trim();
                if (DateTime.TryParse(clean, out var dt))
                {
                    return dt.ToString("yyyy-MM-dd");
                }
                return clean;
            }
            if (!string.IsNullOrWhiteSpace(CreatedAt) && DateTime.TryParse(CreatedAt, out var crDt))
            {
                return crDt.ToString("yyyy-MM-dd");
            }
            return "";
        }
    }

    public string NewExpiryDate { get; set; } = "";

    public string Notes { get; set; } = "";

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public decimal? ExcludingDmg { get; set; }

    public decimal? Profit { get; set; }

    public decimal SimCost { get; set; }

    public decimal CommissionCost { get; set; }

    public decimal AdminCost { get; set; } = 0m;

    public bool IsManualDeviceCost { get; set; } = false;

    public decimal? ManualDeviceCost { get; set; }

    public string Branch { get; set; } = "DMG";

}



public sealed class InvoiceRecord : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    public string DeviceType { get; set; } = "GPS";

    public string InvoiceNo { get; set; } = "";

    public string AckNo { get; set; } = "";

    public string EwayBillNumber { get; set; } = "";

    public int Quantity { get; set; }

    public decimal Rate { get; set; }

    public decimal Amount { get; set; }

    public decimal CourierCharges { get; set; }

    public decimal TotalWithTaxes { get; set; }

    public decimal CostPerDevice { get; set; }

    public int PanicButtonQuantity { get; set; }

    public decimal PanicButtonCost { get; set; }

    public decimal PanicButtonTotal { get; set; }

    public decimal RoundOff { get; set; }

    public string Notes { get; set; } = "";

    public string NetworkOperator { get; set; } = ""; // Taisys / Sensorise

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public string Branch { get; set; } = "DMG";

}



public sealed class ParsedInvoiceDeviceRow

{

    public string Iccid { get; set; } = "";

    public string Imei { get; set; } = "";

    public string Airtel { get; set; } = "";

    public string Bsnl { get; set; } = "";

    public string DeviceSerialNo { get; set; } = "";

}





public sealed class ConfigItem : IBranchEntity

{

    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public string Branch { get; set; } = "DMG";

}



public sealed class CustomDropdownOption : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Category { get; set; } = "";

    public string Value { get; set; } = "";

    public string Branch { get; set; } = "DMG";

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

}



public sealed class VendorRecord : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    public string BatchPrefix { get; set; } = ""; // Custom 3-letter prefix for Batch IDs

    public string Phone { get; set; } = "";

    public string AltPhone { get; set; } = "";

    public string ExtraPhones { get; set; } = ""; // Additional comma-separated phones

    public string WhatsAppNumber { get; set; } = "";

    public bool WhatsAppSameAsPhone { get; set; } = true;

    public string Notes { get; set; } = "";

    public string Branch { get; set; } = "DMG";

    public bool IsOwner { get; set; } = false;

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");



    [NotMapped]

    public string EffectiveBatchPrefix => !string.IsNullOrWhiteSpace(BatchPrefix)

        ? BatchPrefix.Trim().ToUpperInvariant()

        : GenerateDefaultBatchPrefix(Name);



    [NotMapped]

    public string EffectiveWhatsAppNumber => !WhatsAppSameAsPhone && !string.IsNullOrWhiteSpace(WhatsAppNumber)

        ? WhatsAppNumber.Trim()

        : (Phone ?? "").Trim();



    public static string GenerateDefaultBatchPrefix(string? name)

    {

        if (string.IsNullOrWhiteSpace(name)) return "VEN";

        var parts = name.Trim().Split(new[] { ' ', '-', '_', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0) return "VEN";



        if (parts.Length >= 3)

        {

            // e.g. "a m transport" -> "AMT"

            return $"{parts[0][0]}{parts[1][0]}{parts[2][0]}".ToUpperInvariant();

        }

        else if (parts.Length == 2)

        {

            var p1 = parts[0];

            var p2 = parts[1];

            // e.g. "rrl sufion" -> "RRL"

            if (p1.Length == 3)

            {

                return p1.ToUpperInvariant();

            }

            // e.g. "anil blackbuck" or "anil balckbuck" -> 'A' + 'BB' -> "ABB"

            if (p2.Length >= 2)

            {

                return $"{p1[0]}{p2[0]}{p2[1]}".ToUpperInvariant();

            }

            return $"{p1[0]}{p2[0]}X".ToUpperInvariant();

        }

        else

        {

            // Single word e.g. "govesh" -> "GOV"

            var clean = new string(parts[0].Where(char.IsLetterOrDigit).ToArray());

            return clean.Length >= 3 ? clean.Substring(0, 3).ToUpperInvariant() : clean.PadRight(3, 'X').ToUpperInvariant();

        }

    }

}



public sealed class VendorCreditRecord : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Branch { get; set; } = "DMG";

    public string Vendor { get; set; } = "";

    public decimal Amount { get; set; }

    public decimal RemainingBalance { get; set; }

    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    public string PaymentMode { get; set; } = "Banking";

    public string PaidTo { get; set; } = "MYGTS";

    public string Utr { get; set; } = "";

    public string SourceTxnId { get; set; } = "";

    public string Status { get; set; } = "Active"; // "Active", "Settled"

    public string Notes { get; set; } = "";

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public string? SettledAt { get; set; }

}



public sealed class ManualReminder : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";

    public string Details { get; set; } = "";

    public string DueDate { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    public bool IsComplete { get; set; }

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public string Branch { get; set; } = "DMG";

    public string CreatedBy { get; set; } = "";

    public string TargetAudience { get; set; } = "@all";

}



public sealed class RechargeRecord : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string VehicleNo { get; set; } = "";

    public string Imei { get; set; } = "";

    public string Iccid { get; set; } = "";

    public string SimAirtel { get; set; } = "";

    public string SimBsnl { get; set; } = "";

    public string Vendor { get; set; } = "";

    public string NetworkOperator { get; set; } = ""; // Taisys / Sensorise

    public string PaymentStatus { get; set; } = "Not Paid"; // Paid / Not Paid

    public string RechargeStatus { get; set; } = "Pending"; // Processed / Pending

    public string RechargeDate { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    public string ExpiryDate { get; set; } = "";

    public string PreviousExpiryDate { get; set; } = "";

    public string ActivationDate { get; set; } = "";



    public decimal AmountPaid { get; set; } = 0;

    public string Notes { get; set; } = "";

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public string Branch { get; set; } = "DMG";

}



public sealed class CompanyRechargeBatch : IBranchEntity

{

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Branch { get; set; } = "DMG";

    public string PoNumber { get; set; } = "";

    public string PayReferenceNo { get; set; } = "";

    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    public string Provider { get; set; } = "Taisys";

    public decimal AdvanceAmount { get; set; } = 0m;

    public decimal CarriedForwardAmount { get; set; } = 0m;

    public decimal CarriedOutAmount { get; set; } = 0m;

    public string? TransferredFromBatchId { get; set; }

    public string? TransferredFromBatchPo { get; set; }

    public string? TransferredToBatchId { get; set; }

    public string? TransferredToBatchPo { get; set; }

    public decimal TotalAmount { get; set; } = 0m;

    public decimal RemainingBalance { get; set; } = 0m;

    public int TotalDevices { get; set; } = 0;

    public int RenewalCount { get; set; } = 0;

    public int ReactivationCount { get; set; } = 0;

    public string Notes { get; set; } = "";

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public List<CompanyRechargeItem> Items { get; set; } = new();



    [NotMapped]

    public decimal EffectiveAdvance => AdvanceAmount + CarriedForwardAmount - CarriedOutAmount;

    [NotMapped]

    public string BatchDate { get => Date; set => Date = value; }

    [NotMapped]

    public string PaymentReferenceNo { get => PayReferenceNo; set => PayReferenceNo = value; }

}



public sealed class CompanyRechargeItem : IBranchEntity

{

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Branch { get; set; } = "DMG";

    public string BatchId { get; set; } = "";

    public int SlNo { get; set; } = 1;

    public string VehicleNo { get; set; } = "";

    public string Imei { get; set; } = "";

    public string Iccid { get; set; } = "";

    public string RequestId { get; set; } = "-";

    public string ActionType { get; set; } = "Renewal"; // "Renewal" or "Reactivation"

    public decimal PerDeviceAmount { get; set; } = 1298m;

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

}



public sealed class DateRange

{

    public DateTime? Start { get; init; }

    public DateTime? End { get; init; }

}



public static class DateValue

{

    private static readonly string[] CustomFormats = new[]

    {

        "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy", "dd/MM/yyyy",

        "d-M-yyyy", "d/M/yyyy", "d-MM-yyyy", "dd-M-yyyy", "d/MM/yyyy", "dd/M/yyyy",

        "yyyy-M-d", "yyyy/M/d", "dd-MMM-yyyy", "dd MMM yyyy", "d-MMM-yyyy", "d MMM yyyy",

        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-dd HH:mm:ss",

        "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy", "M-d-yyyy"

    };



    public static DateTime? Parse(string? value)

    {

        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();

        if (DateTime.TryParseExact(trimmed, CustomFormats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var exactResult))

        {

            return exactResult.Date;

        }

        if (DateTime.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var invResult))

        {

            return invResult.Date;

        }

        if (DateTime.TryParse(trimmed, out var result))

        {

            return result.Date;

        }

        return null;

    }



    public static string Short(string? value)

        => Parse(value)?.ToString("dd MMM yyyy") ?? "—";



    public static int? DaysFromNow(string? value)

    {

        var parsed = Parse(value);

        return parsed is null ? null : (parsed.Value.Date - DateTime.UtcNow.Date).Days;

    }



    /// <summary>

    /// For DMG branch: calculate expiry date based on activation date (12 months).

    /// </summary>

    public static string? CalculateDmgExpiry(string? activationDate)

    {

        var parsed = Parse(activationDate);

        return parsed is null ? null : parsed.Value.AddMonths(12).ToString("yyyy-MM-dd");

    }

}



public static class DeviceLifecycleHelper

{

    public const string StatusActive = "Active";

    public const string StatusSuspension = "Suspension";

    public const string StatusTerminated = "Terminated";

    public const string StatusDead = "Dead";

    public const string StatusInventory = "Inventory";



    public static (string VehicleNo, string? PreviousDevice) ParseVehicleSlash(string? rawVehicleNo)

    {

        if (string.IsNullOrWhiteSpace(rawVehicleNo)) return ("", null);

        var clean = rawVehicleNo.Trim().TrimStart('`').Trim();

        if (!clean.Contains('/')) return (clean, null);



        var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0) return ("", null);

        if (parts.Length == 1) return (parts[0], null);



        var lastPart = parts[^1];

        var firstPart = string.Join('/', parts[..^1]);



        if (string.Equals(lastPart, "Theft", StringComparison.OrdinalIgnoreCase) || 

            string.Equals(lastPart, "Theft Vehicle", StringComparison.OrdinalIgnoreCase))

        {

            return (parts[0], null);

        }



        if (lastPart.Length <= 2 && int.TryParse(lastPart, out _))

        {

            if (lastPart == "1") return (parts[0], null);

            var prefix = parts[0][..^lastPart.Length];

            return (prefix + lastPart, parts[0]);

        }



        return (lastPart, firstPart);

    }



    public static readonly Dictionary<string, string> DmgDefaults = new(StringComparer.OrdinalIgnoreCase)

    {

        ["ADMIN_COST"] = "0.00",

        ["PRICE_RECHARGE"] = "2500.00",

        ["PRICE_REACTIVATION"] = "3500.00",

        ["PRICE_TAX"] = "0.18",

        ["REMINDER_DAYS"] = "30",

        ["SUSPENSION_DAYS"] = "30",

        ["TERMINATION_DAYS"] = "20",

        ["SENSORISE_SUSPENSION_DAYS"] = "30",

        ["SENSORISE_TERMINATION_DAYS"] = "20",

        ["ACTIVATION_EXPIRY_MONTHS"] = "12",

        ["ACTIVATION_EXPIRY_DAYS"] = "0",

        ["COMPANY_RECHARGE_RATE"] = "1298.00",

        ["COMPANY_REACTIVATION_RATE"] = "1534.00",

        ["BLUEMINE_RECHARGE_RATE"] = "1156.40",

        ["BLUEMINE_REACTIVATION_RATE"] = "1392.40",

        ["SMT_RECHARGE_RATE"] = "1298.00",

        ["SMT_REACTIVATION_RATE"] = "1534.00",

        ["SENSORISE_RECHARGE_RATE"] = "1298.00",

        ["SENSORISE_REACTIVATION_RATE"] = "1534.00"

    };



    public static readonly Dictionary<string, string> RtoDefaults = new(StringComparer.OrdinalIgnoreCase)

    {

        ["ADMIN_COST"] = "0.00",

        ["PRICE_RECHARGE"] = "2000.00",

        ["PRICE_REACTIVATION"] = "3000.00",

        ["PRICE_TAX"] = "0.18",

        ["SIM_COST"] = "1300.00",

        ["SIM_COST_1_YEAR"] = "650.00",

        ["COMMISSION_COST"] = "500.00",

        ["REMINDER_DAYS"] = "30",

        ["SUSPENSION_DAYS"] = "30",

        ["TERMINATION_DAYS"] = "20",

        ["SENSORISE_SUSPENSION_DAYS"] = "30",

        ["SENSORISE_TERMINATION_DAYS"] = "20",

        ["ACTIVATION_EXPIRY_1_YEAR_MONTHS"] = "12",

        ["ACTIVATION_EXPIRY_1_YEAR_DAYS"] = "0",

        ["ACTIVATION_EXPIRY_2_YEAR_MONTHS"] = "24",

        ["ACTIVATION_EXPIRY_2_YEAR_DAYS"] = "0",

        ["ACTIVATION_EXPIRY_MONTHS"] = "12",

        ["ACTIVATION_EXPIRY_DAYS"] = "0",

        ["COMPANY_RECHARGE_RATE"] = "1298.00",

        ["COMPANY_REACTIVATION_RATE"] = "1534.00",

        ["BLUEMINE_RECHARGE_RATE"] = "1156.40",

        ["BLUEMINE_REACTIVATION_RATE"] = "1392.40",

        ["SMT_RECHARGE_RATE"] = "1298.00",

        ["SMT_REACTIVATION_RATE"] = "1534.00",

        ["SENSORISE_RECHARGE_RATE"] = "1298.00",

        ["SENSORISE_REACTIVATION_RATE"] = "1534.00"

    };





    public static decimal ResolveRechargeCost(string? networkOperator, Dictionary<string, string> config, string? paymentFor)

    {

        var isReactivation = string.Equals(paymentFor, "Reactivation", StringComparison.OrdinalIgnoreCase);

        var op = (networkOperator ?? "").Trim().ToLowerInvariant();



        if (op.Contains("babu") || op.Contains("bluemine"))

        {

            var key = isReactivation ? "BLUEMINE_REACTIVATION_RATE" : "BLUEMINE_RECHARGE_RATE";

            if (config.TryGetValue(key, out var val) && decimal.TryParse(val, out var parsed) && parsed > 0) return parsed;

            var altKey = isReactivation ? "TAISYS_BABU_REACTIVATION_RATE" : "TAISYS_BABU_RECHARGE_RATE";

            if (config.TryGetValue(altKey, out var altVal) && decimal.TryParse(altVal, out var parsedAlt) && parsedAlt > 0) return parsedAlt;

            return isReactivation ? 1392.40m : 1156.40m;

        }



        if (op.Contains("smt"))

        {

            var key = isReactivation ? "SMT_REACTIVATION_RATE" : "SMT_RECHARGE_RATE";

            if (config.TryGetValue(key, out var val) && decimal.TryParse(val, out var parsed) && parsed > 0) return parsed;

        }



        if (op.Contains("sensorise"))

        {

            var key = isReactivation ? "SENSORISE_REACTIVATION_RATE" : "SENSORISE_RECHARGE_RATE";

            if (config.TryGetValue(key, out var val) && decimal.TryParse(val, out var parsed) && parsed > 0) return parsed;

        }



        if (op.Contains("mygts") || op.Contains("taisys"))

        {

            var key = isReactivation ? "COMPANY_REACTIVATION_RATE" : "COMPANY_RECHARGE_RATE";

            if (config.TryGetValue(key, out var val) && decimal.TryParse(val, out var parsed) && parsed > 0) return parsed;

            return isReactivation ? 1534.00m : 1298.00m;

        }



        // Generic fallback

        var defKey = isReactivation ? "PRICE_REACTIVATION" : "PRICE_RECHARGE";

        if (config.TryGetValue(defKey, out var defVal) && decimal.TryParse(defVal, out var parsedDef) && parsedDef > 0) return parsedDef;

        return isReactivation ? 1534.00m : 1298.00m;

    }

    public static Dictionary<string, string> BuildMergedConfig(string? branch, IEnumerable<ConfigItem>? dbConfig = null)

    {

        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);

        var merged = new Dictionary<string, string>(isRto ? RtoDefaults : DmgDefaults, StringComparer.OrdinalIgnoreCase);

        if (dbConfig != null)

        {

            foreach (var item in dbConfig)

            {

                if (!string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))

                {

                    merged[item.Key] = item.Value.Trim();

                }

            }

        }

        return merged;

    }



    public static string? CalculateExpiry(DeviceRecord device, IReadOnlyDictionary<string, string>? config = null)
    {
        DateTime? existingExpiry = null;
        if (!string.IsNullOrWhiteSpace(device.LatestExpiryDate))
        {
            existingExpiry = DateValue.Parse(device.LatestExpiryDate);
        }

        DateTime? fromActivation = null;
        if (!string.IsNullOrWhiteSpace(device.ActivationDate))
        {
            var calculated = CalculateExpiryFromActivation(device.ActivationDate, device.Branch, device.Type ?? device.InstallType, config);
            if (!string.IsNullOrWhiteSpace(calculated))
            {
                fromActivation = DateValue.Parse(calculated);
            }
        }

        // Never downgrade an existing valid expiry date (e.g. telecom 2-year subscriptions or operator renewals)
        if (existingExpiry.HasValue && fromActivation.HasValue)
        {
            return (existingExpiry.Value >= fromActivation.Value ? existingExpiry.Value : fromActivation.Value).ToString("yyyy-MM-dd");
        }

        if (existingExpiry.HasValue) return existingExpiry.Value.ToString("yyyy-MM-dd");
        if (fromActivation.HasValue) return fromActivation.Value.ToString("yyyy-MM-dd");

        return null;
    }

    public static string? CalculateExpiryFromActivation(string? activationDate, string? branch, string? type, IReadOnlyDictionary<string, string>? config = null)

    {

        if (string.IsNullOrWhiteSpace(activationDate)) return null;



        var actDate = DateValue.Parse(activationDate);

        if (!actDate.HasValue) return null;



        if (string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase))

        {

            bool isTwoYear = string.Equals(type, "New", StringComparison.OrdinalIgnoreCase)

                          || string.Equals(type, "New Installation", StringComparison.OrdinalIgnoreCase)

                          || (type != null && (type.Contains("NEW", StringComparison.OrdinalIgnoreCase) || type.Contains('2')));



            int months;

            int days;



            if (isTwoYear)

            {

                months = 24;

                days = 0;



                if (config != null)

                {

                    if (config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_MONTHS", out var m2) && int.TryParse(m2, out var v2)) months = v2;

                    else if (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var mf) && int.TryParse(mf, out var vf)) months = vf > 12 ? vf : vf + 12;



                    if (config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_DAYS", out var d2) && int.TryParse(d2, out var vd2)) days = vd2;

                    else if (config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var df) && int.TryParse(df, out var vdf)) days = vdf;

                }

            }

            else

            {

                months = 12;

                days = 0;



                if (config != null)

                {

                    if (config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_MONTHS", out var m1) && int.TryParse(m1, out var v1)) months = v1;

                    else if (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var mf) && int.TryParse(mf, out var vf)) months = vf > 12 ? vf / 2 : vf;



                    if (config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_DAYS", out var d1) && int.TryParse(d1, out var vd1)) days = vd1;

                    else if (config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var df) && int.TryParse(df, out var vdf)) days = vdf;

                }

            }



            return actDate.Value.AddMonths(months).AddDays(days).ToString("yyyy-MM-dd");

        }

        else
        {
            // DMG branch: standard validity period is always 1 year from activation date
            return actDate.Value.AddYears(1).ToString("yyyy-MM-dd");
        }

    }



    public static bool IsActivated(DeviceRecord device)

    {

        // Only if the device has a vehicle number should it be considered in fleet.

        return !string.IsNullOrWhiteSpace(device.VehicleNo);

    }



    public static (string LifecycleStatus, int DaysPastExpiry) Evaluate(DeviceRecord device, IReadOnlyDictionary<string, string>? config = null, DateTime? referenceDate = null)

    {

        if (string.Equals(device.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase))

        {

            return ("Replaced", device.DaysPastExpiry ?? 0);

        }



        if (device.IsNotInUse)

        {

            return ("Not In Use", 0);

        }



        // Only if the device has a vehicle number should it be considered in fleet.

        // A device without a vehicle number (even if activated or in stock) belongs strictly to Inventory.

        if (string.IsNullOrWhiteSpace(device.VehicleNo))

        {

            return (StatusInventory, 0);

        }



        var today = (referenceDate ?? DateTime.UtcNow).Date;

        var expiryStr = CalculateExpiry(device, config);

        var expiryDate = DateValue.Parse(expiryStr);



        if (!expiryDate.HasValue)

        {

            return (StatusActive, 0);

        }



        int daysPastExpiry = (today - expiryDate.Value.Date).Days;



        if (daysPastExpiry <= 0)

        {

            // Within expiry date -> Active (not Inventory)

            return (StatusActive, daysPastExpiry);

        }



        // Exceeded expiry date: follow remainder/reminder windows

        bool isSensorise = string.Equals(device.NetworkOperator, "Sensorise", StringComparison.OrdinalIgnoreCase);



        int suspensionDays = 30;

        int terminationDays = 20;



        if (config != null)

        {

            if (isSensorise)

            {

                if (config.TryGetValue("SENSORISE_SUSPENSION_DAYS", out var ssStr) && int.TryParse(ssStr, out var ssVal))

                    suspensionDays = ssVal;

                else if (config.TryGetValue("SUSPENSION_DAYS", out var sStr) && int.TryParse(sStr, out var sVal))

                    suspensionDays = sVal;



                if (config.TryGetValue("SENSORISE_TERMINATION_DAYS", out var stStr) && int.TryParse(stStr, out var stVal))

                    terminationDays = stVal;

                else if (config.TryGetValue("TERMINATION_DAYS", out var tStr) && int.TryParse(tStr, out var tVal))

                    terminationDays = tVal;

            }

            else

            {

                if (config.TryGetValue("SUSPENSION_DAYS", out var sStr) && int.TryParse(sStr, out var sVal))

                    suspensionDays = sVal;

                if (config.TryGetValue("TERMINATION_DAYS", out var tStr) && int.TryParse(tStr, out var tVal))

                    terminationDays = tVal;

            }

        }



        if (daysPastExpiry <= suspensionDays)

        {

            return (StatusSuspension, daysPastExpiry);

        }

        else if (daysPastExpiry <= (suspensionDays + terminationDays))

        {

            return (StatusTerminated, daysPastExpiry);

        }

        else

        {

            return (StatusDead, daysPastExpiry);

        }

    }



    public static void SyncDevice(DeviceRecord device, IReadOnlyDictionary<string, string>? config = null, DateTime? referenceDate = null)

    {

        var expiry = CalculateExpiry(device, config);

        if (!string.IsNullOrWhiteSpace(expiry))

        {

            device.LatestExpiryDate = expiry;

        }



        var (status, daysPast) = Evaluate(device, config, referenceDate);

        device.LifecycleStatus = status;

        device.Status = status;

        device.DaysPastExpiry = daysPast;

    }



    public static (string ActivationDate, string NewExpiryDate, bool FromRecharge) ResolveDatesForTransaction(
        string? rawVehicleNo,
        DateTime txnDate,
        string branch,
        IEnumerable<RechargeRecord>? recharges,
        IEnumerable<DeviceRecord>? devices,
        IReadOnlyDictionary<string, string>? config = null,
        string? paymentFor = null,
        string? imei = null)
    {
        var clean = (rawVehicleNo ?? "").Replace(" ", "").Trim();
        var cleanImei = (imei ?? "").Trim();
        var isActivation = string.Equals(paymentFor, "Activation", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(paymentFor, "NEW DEVICE", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(paymentFor, "New", StringComparison.OrdinalIgnoreCase);
        var isOther = string.Equals(paymentFor, "Other", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(paymentFor, "Others", StringComparison.OrdinalIgnoreCase);
        var isDmg = string.Equals(branch, "DMG", StringComparison.OrdinalIgnoreCase);
        var defaultAct = txnDate.ToString("yyyy-MM-dd");

        // 1. Look for existing Recharge record for this vehicle/IMEI in Recharges page
        RechargeRecord? rec = null;
        if (recharges != null)
        {
            if (!string.IsNullOrWhiteSpace(cleanImei))
            {
                rec = recharges
                    .Where(r => string.Equals((r.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.RechargeDate)
                    .ThenByDescending(r => r.CreatedAt)
                    .FirstOrDefault();
            }
            if (rec == null && !string.IsNullOrWhiteSpace(clean))
            {
                rec = recharges
                    .Where(r => string.Equals(r.VehicleNo?.Replace(" ", "").Trim(), clean, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.RechargeDate)
                    .ThenByDescending(r => r.CreatedAt)
                    .FirstOrDefault();
            }
        }

        // 2. Find device record
        DeviceRecord? dev = null;
        if (devices != null)
        {
            if (!string.IsNullOrWhiteSpace(cleanImei))
            {
                dev = devices.FirstOrDefault(d => string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase));
            }
            if (dev == null && !string.IsNullOrWhiteSpace(clean))
            {
                dev = devices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), clean, StringComparison.OrdinalIgnoreCase));
            }
        }

        // If PaymentFor is "Other" / "Others", do not alter or recalculate expiry date
        if (isOther)
        {
            var curAct = !string.IsNullOrWhiteSpace(dev?.ActivationDate) ? DateValue.Parse(dev.ActivationDate)?.ToString("yyyy-MM-dd") : defaultAct;
            var curExp = !string.IsNullOrWhiteSpace(dev?.LatestExpiryDate) ? DateValue.Parse(dev.LatestExpiryDate)?.ToString("yyyy-MM-dd") : (rec?.ExpiryDate ?? "");
            return (curAct ?? defaultAct, curExp ?? "", false);
        }

        // DMG Branch Rule: All payment options except "Other" update expiry to exactly 1 year from activation date
        if (isDmg)
        {
            var isRecharge = !string.IsNullOrWhiteSpace(paymentFor) && (paymentFor.Contains("Recharge", StringComparison.OrdinalIgnoreCase) || paymentFor.Contains("Renewal", StringComparison.OrdinalIgnoreCase) || paymentFor.Contains("Reactivation", StringComparison.OrdinalIgnoreCase));

            // If a recharge record exists with dates, prioritize them
            if (rec != null && !string.IsNullOrWhiteSpace(rec.ExpiryDate))
            {
                var rExp = DateValue.Parse(rec.ExpiryDate)?.ToString("yyyy-MM-dd") ?? rec.ExpiryDate;
                var rAct = !string.IsNullOrWhiteSpace(rec.ActivationDate)
                    ? (DateValue.Parse(rec.ActivationDate)?.ToString("yyyy-MM-dd") ?? rec.ActivationDate)
                    : (DateValue.Parse(rExp)?.AddYears(-1).ToString("yyyy-MM-dd") ?? defaultAct);
                return (rAct, rExp, true);
            }

            if (dev != null && !string.IsNullOrWhiteSpace(dev.LatestExpiryDate))
            {
                var devExpParsed = DateValue.Parse(dev.LatestExpiryDate);
                var devActParsed = DateValue.Parse(dev.ActivationDate);

                if (isRecharge)
                {
                    // If device is active and not expired, extend 1 year, otherwise 1 year from txnDate
                    DateTime newExpDt;
                    if (devExpParsed.HasValue && devExpParsed.Value.Date >= txnDate.Date)
                    {
                        // Extend from current expiry, but cap at 1 year from txnDate if already far in future
                        newExpDt = devExpParsed.Value.Year >= (txnDate.Year + 2) ? txnDate.AddYears(1) : devExpParsed.Value.AddYears(1);
                    }
                    else
                    {
                        newExpDt = txnDate.AddYears(1);
                    }
                    var newActDt = newExpDt.AddYears(-1);
                    return (newActDt.ToString("yyyy-MM-dd"), newExpDt.ToString("yyyy-MM-dd"), false);
                }
                else if (isActivation)
                {
                    return (txnDate.ToString("yyyy-MM-dd"), txnDate.AddYears(1).ToString("yyyy-MM-dd"), false);
                }
                else
                {
                    // Existing device dates: ensure activation date is within 1 year of expiry
                    var expStr = devExpParsed.HasValue ? devExpParsed.Value.ToString("yyyy-MM-dd") : dev.LatestExpiryDate;
                    var actStr = (devExpParsed.HasValue && (!devActParsed.HasValue || (devExpParsed.Value.Date - devActParsed.Value.Date).TotalDays > 400))
                        ? devExpParsed.Value.AddYears(-1).ToString("yyyy-MM-dd")
                        : (devActParsed.HasValue ? devActParsed.Value.ToString("yyyy-MM-dd") : defaultAct);
                    return (actStr, expStr, false);
                }
            }

            var act = !string.IsNullOrWhiteSpace(dev?.ActivationDate) ? DateValue.Parse(dev.ActivationDate)?.ToString("yyyy-MM-dd") : defaultAct;
            var exp = CalculateExpiryFromActivation(act, "DMG", dev?.Type, config) ?? (DateValue.Parse(act) ?? txnDate).AddYears(1).ToString("yyyy-MM-dd");
            return (act ?? defaultAct, exp, false);
        }

        // RTO Branch Rules:
        // Type = New: expiry date 2 years from activation date
        // Type = Old (or non-New): expiry date 1 year from previous expiry date
        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);
        if (isRto)
        {
            var act = !string.IsNullOrWhiteSpace(dev?.ActivationDate)
                ? DateValue.Parse(dev.ActivationDate)?.ToString("yyyy-MM-dd")
                : (!string.IsNullOrWhiteSpace(rec?.ActivationDate)
                    ? DateValue.Parse(rec.ActivationDate)?.ToString("yyyy-MM-dd")
                    : defaultAct);
            var parsedAct = DateValue.Parse(act) ?? txnDate;

            bool isRtoNew = string.Equals(dev?.Type, "New", StringComparison.OrdinalIgnoreCase)
                         || (dev?.Type != null && (dev.Type.Contains("NEW", StringComparison.OrdinalIgnoreCase) || dev.Type.Contains('2')))
                         || isActivation;

            if (isRtoNew)
            {
                // New: exactly 2 years from the activation date
                var exp = CalculateExpiryFromActivation(act, "RTO", "New", config) ?? parsedAct.AddYears(2).ToString("yyyy-MM-dd");
                return (act ?? defaultAct, exp, false);
            }
            else
            {
                // Old: exactly 1 year from previous expiry date
                DateTime? prevExp = null;
                if (!string.IsNullOrWhiteSpace(dev?.LatestExpiryDate))
                {
                    prevExp = DateValue.Parse(dev.LatestExpiryDate);
                }
                else if (rec != null && !string.IsNullOrWhiteSpace(rec.ExpiryDate))
                {
                    prevExp = DateValue.Parse(rec.ExpiryDate);
                }
                else if (rec != null && !string.IsNullOrWhiteSpace(rec.PreviousExpiryDate))
                {
                    prevExp = DateValue.Parse(rec.PreviousExpiryDate);
                }

                if (prevExp.HasValue)
                {
                    var exp = prevExp.Value.AddYears(1).ToString("yyyy-MM-dd");
                    return (act ?? defaultAct, exp, false);
                }
                else
                {
                    var exp = CalculateExpiryFromActivation(act, "RTO", "Old", config) ?? parsedAct.AddYears(1).ToString("yyyy-MM-dd");
                    return (act ?? defaultAct, exp, false);
                }
            }
        }

        var fallbackAct = !string.IsNullOrWhiteSpace(dev?.ActivationDate) ? DateValue.Parse(dev.ActivationDate)?.ToString("yyyy-MM-dd") : defaultAct;
        var fallbackExp = (DateValue.Parse(fallbackAct) ?? txnDate).AddYears(1).ToString("yyyy-MM-dd");
        return (fallbackAct ?? defaultAct, fallbackExp, false);
    }

}



public sealed class LifecycleShiftAlert

{

    public string VehicleNo { get; set; } = "";

    public string Owner { get; set; } = "";

    public string CellNo { get; set; } = "";

    public string Vendor { get; set; } = "";

    public string FromStatus { get; set; } = "";

    public string ToStatus { get; set; } = "";

    public string ShiftType { get; set; } = "";

    public string Description { get; set; } = "";

    public string FilterUrl { get; set; } = "";

    public string ExpiryStr { get; set; } = "";

    public int DaysPast { get; set; }

}



public sealed class DataShareRecord : IBranchEntity

{

    [Key]

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Branch { get; set; } = "DMG";

    public string VehicleNo { get; set; } = "";

    public string Imei { get; set; } = "";

    public string Iccid { get; set; } = "";

    public string DeviceName { get; set; } = "";

    public string DeviceBranch { get; set; } = "";

    public string SharedBy { get; set; } = "";

    public string SharedByEmail { get; set; } = "";

    public string SharedByRole { get; set; } = "";

    public string SharedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public string Recipient { get; set; } = "";

    public string Notes { get; set; } = "";



    [NotMapped]

    public string SharedAtFormatted

    {

        get

        {

            if (DateTime.TryParse(SharedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))

            {

                return dt.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt");

            }

            return SharedAt;

        }

    }

}









public sealed class TxnMergePart
{
    public string Date { get; set; } = "—";
    public decimal Amount { get; set; }
    public string Mode { get; set; } = "—";
    public string Utr { get; set; } = "—";
    public string PaidTo { get; set; } = "—";
}

public static class TxnMergeHelper
{
    public static List<TxnMergePart>? ParseMergeParts(string? notes, string? utr, string? paymentMode, string? paidTo, decimal? totalAmount = null)
    {
        var list = new List<TxnMergePart>();
        
        // 1. Check for JSON in [MERGE_PARTS: ... ]
        if (!string.IsNullOrWhiteSpace(notes) && notes.Contains("[MERGE_PARTS:"))
        {
            try
            {
                var s = notes.IndexOf("[MERGE_PARTS:") + 13;
                var curr = s;
                var bracketCount = 0;
                while (curr < notes.Length)
                {
                    if (notes[curr] == '[') bracketCount++;
                    else if (notes[curr] == ']')
                    {
                        bracketCount--;
                        if (bracketCount == 0)
                        {
                            curr++;
                            break;
                        }
                    }
                    curr++;
                }
                var jsonStr = notes.Substring(s, curr - s);
                var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var amt = el.TryGetProperty("amount", out var aProp) ? aProp.GetDecimal() : 0m;
                    var md = el.TryGetProperty("mode", out var mProp) ? (mProp.GetString() ?? "—") : "—";
                    if (md.Equals("Online", StringComparison.OrdinalIgnoreCase)) md = "Banking";
                    var u = el.TryGetProperty("utr", out var uProp) ? (uProp.GetString() ?? "—") : "—";
                    var dt = el.TryGetProperty("date", out var dProp) ? (dProp.GetString() ?? "—") : "—";
                    var pt = el.TryGetProperty("paidTo", out var ptProp) ? (ptProp.GetString() ?? (paidTo ?? "—")) : (paidTo ?? "—");
                    list.Add(new TxnMergePart { Date = dt, Amount = amt, Mode = md.ToUpperInvariant(), Utr = u, PaidTo = pt });
                }
                if (list.Count > 0)
                {
                    if (totalAmount.HasValue && totalAmount.Value > 0 && list.All(p => p.Amount <= 0))
                    {
                        var split = Math.Round(totalAmount.Value / list.Count, 2);
                        foreach (var p in list) p.Amount = split;
                    }
                    return list;
                }
            }
            catch { }
        }

        // 2. Check for Split Payment: in notes (e.g. Split Payment: 4500 (Online) + 6000 (Online))
        if (!string.IsNullOrWhiteSpace(notes) && notes.Contains("Split Payment:"))
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(notes, @"Split Payment:\s*([^\[\r\n]+)");
                if (match.Success)
                {
                    var splitText = match.Groups[1].Value;
                    var itemMatches = System.Text.RegularExpressions.Regex.Matches(splitText, @"(\d+(?:\.\d+)?)\s*\(([^)]+)\)");
                    var utrParts = !string.IsNullOrWhiteSpace(utr) ? utr.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : new string[0];
                    for (int i = 0; i < itemMatches.Count; i++)
                    {
                        var amt = decimal.Parse(itemMatches[i].Groups[1].Value);
                        var md = itemMatches[i].Groups[2].Value.Trim();
                        if (md.Equals("Online", StringComparison.OrdinalIgnoreCase)) md = "Banking";
                        var u = i < utrParts.Length ? utrParts[i] : (utr ?? "—");
                        list.Add(new TxnMergePart { Amount = amt, Mode = md.ToUpperInvariant(), Utr = u, PaidTo = paidTo ?? "—" });
                    }
                    if (list.Count > 0)
                {
                    if (totalAmount.HasValue && totalAmount.Value > 0 && list.All(p => p.Amount <= 0))
                    {
                        var split = Math.Round(totalAmount.Value / list.Count, 2);
                        foreach (var p in list) p.Amount = split;
                    }
                    return list;
                }
                }
            }
            catch { }
        }

        // 3. Fallback: check if PaymentMode or Utr contains '+'
        if ((!string.IsNullOrWhiteSpace(paymentMode) && paymentMode.Contains('+')) || (!string.IsNullOrWhiteSpace(utr) && utr.Contains('+')))
        {
            var mParts = !string.IsNullOrWhiteSpace(paymentMode) ? paymentMode.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : new string[0];
            var uParts = !string.IsNullOrWhiteSpace(utr) ? utr.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : new string[0];
            int count = Math.Max(mParts.Length, uParts.Length);
            if (count > 1)
            {
                decimal defaultAmt = 0m;
                if (totalAmount.HasValue && totalAmount.Value > 0)
                {
                    defaultAmt = Math.Round(totalAmount.Value / count, 2);
                }
                for (int i = 0; i < count; i++)
                {
                    var md = i < mParts.Length ? mParts[i].Trim() : "—";
                    if (md.Equals("Online", StringComparison.OrdinalIgnoreCase)) md = "Banking";
                    var u = i < uParts.Length ? uParts[i].Trim() : (utr ?? "—");
                    list.Add(new TxnMergePart { Amount = defaultAmt, Mode = md.ToUpperInvariant(), Utr = u, PaidTo = paidTo ?? "—" });
                }
                return list;
            }
        }

        return null;
    }

    public static (string CleanNotes, decimal? TotalPayment, List<TxnMergePart> Parts) ExtractMergeDetails(string? notes, decimal? amount, string? utr, string? paymentMode, string? paidTo)
    {
        var rawNotes = notes ?? "";
        var parts = ParseMergeParts(rawNotes, utr, paymentMode, paidTo, amount) ?? new List<TxnMergePart>();
        decimal partsSum = parts.Sum(p => p.Amount);
        if (partsSum == 0 && amount.HasValue && amount.Value > 0)
        {
            partsSum = amount.Value;
        }

        decimal? totalPayment = null;

        var pbMatch = System.Text.RegularExpressions.Regex.Match(rawNotes, @"\[PENDING_BALANCE:\s*₹?([0-9,]+(?:\.[0-9]+)?)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (pbMatch.Success && decimal.TryParse(pbMatch.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pending))
        {
            totalPayment = partsSum + pending;
        }
        else
        {
            var opMatch = System.Text.RegularExpressions.Regex.Match(rawNotes, @"\[OVERPAID:\s*₹?([0-9,]+(?:\.[0-9]+)?)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (opMatch.Success && decimal.TryParse(opMatch.Groups[1].Value.Replace(",", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var overpaid))
            {
                totalPayment = Math.Max(0, partsSum - overpaid);
            }
            else if (parts.Count > 1 || rawNotes.Contains("[MERGE_PARTS:"))
            {
                totalPayment = partsSum > 0 ? partsSum : amount;
            }
        }

        var cleanNotes = rawNotes;
        if (cleanNotes.Contains("[MERGE_PARTS:"))
        {
            cleanNotes = System.Text.RegularExpressions.Regex.Replace(cleanNotes, @"Split Payment:\s*[^\[\r\n]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            cleanNotes = System.Text.RegularExpressions.Regex.Replace(cleanNotes, @"\[MERGE_PARTS:\[[\s\S]*?\]\]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        else
        {
            cleanNotes = System.Text.RegularExpressions.Regex.Replace(cleanNotes, @"Split Payment:\s*(?:\d+(?:\.\d+)?\s*\([^)]+\)(?:\s*\+\s*)?)+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        cleanNotes = System.Text.RegularExpressions.Regex.Replace(cleanNotes, @"\[PENDING_BALANCE:\s*₹?[0-9,]+(?:\.[0-9]+)?\]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleanNotes = System.Text.RegularExpressions.Regex.Replace(cleanNotes, @"\[OVERPAID:\s*₹?[0-9,]+(?:\.[0-9]+)?\]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleanNotes = cleanNotes.Trim();

        return (cleanNotes, totalPayment, parts);
    }
}

public sealed class TxnBulkPart
{
    public string VehicleNo { get; set; } = "";
    public string Imei { get; set; } = "";
    public string Operator { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Profit { get; set; }
    public string ActivationDate { get; set; } = "";
    public string NewExpiryDate { get; set; } = "";
}

public static class TxnBulkHelper
{
    public static List<TxnBulkPart>? ParseBulkParts(string? notes, decimal? totalAmount = null, decimal? totalProfit = null)
    {
        if (string.IsNullOrWhiteSpace(notes) || !notes.Contains("[BULK_PARTS:"))
            return null;

        try
        {
            var s = notes.IndexOf("[BULK_PARTS:") + 12;
            var curr = s;
            var bracketCount = 0;
            while (curr < notes.Length)
            {
                if (notes[curr] == '[') bracketCount++;
                else if (notes[curr] == ']')
                {
                    bracketCount--;
                    if (bracketCount == 0)
                    {
                        curr++;
                        break;
                    }
                }
                curr++;
            }
            var jsonStr = notes.Substring(s, curr - s);
            var list = new List<TxnBulkPart>();
            using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var veh = el.TryGetProperty("vehicleNo", out var vProp) ? (vProp.GetString() ?? "") : "";
                var imei = el.TryGetProperty("imei", out var iProp) ? (iProp.GetString() ?? "") : "";
                var op = el.TryGetProperty("operator", out var oProp) ? (oProp.GetString() ?? "") : "";
                var amt = el.TryGetProperty("amount", out var aProp) ? aProp.GetDecimal() : 0m;
                var prf = el.TryGetProperty("profit", out var pProp) ? pProp.GetDecimal() : 0m;
                var act = el.TryGetProperty("activationDate", out var actProp) ? (actProp.GetString() ?? "") : "";
                var exp = el.TryGetProperty("newExpiryDate", out var expProp) ? (expProp.GetString() ?? "") : "";
                list.Add(new TxnBulkPart
                {
                    VehicleNo = veh,
                    Imei = imei,
                    Operator = op,
                    Amount = amt,
                    Profit = prf,
                    ActivationDate = act,
                    NewExpiryDate = exp
                });
            }
            if (list.Count > 0)
            {
                if (totalAmount.HasValue && totalAmount.Value > 0 && list.All(p => p.Amount <= 0))
                {
                    var splitAmt = Math.Round(totalAmount.Value / list.Count, 2);
                    foreach (var p in list) p.Amount = splitAmt;
                }
                if (totalProfit.HasValue && list.All(p => p.Profit <= 0))
                {
                    var splitPrf = Math.Round(totalProfit.Value / list.Count, 2);
                    foreach (var p in list) p.Profit = splitPrf;
                }
                return list;
            }
        }
        catch { }
        return null;
    }
}

public class QuickExpiryUpdateRequest
{
    public string Id { get; set; } = "";
    public string NewExpiryDate { get; set; } = "";
    public bool ApplyToBatch { get; set; }
}
