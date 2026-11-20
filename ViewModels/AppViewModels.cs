using System.ComponentModel.DataAnnotations;
using MygtsMvc.Models;

namespace MygtsMvc.ViewModels;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Please enter your username or email.")]
    [Display(Name = "Username or Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Please enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Branch")]
    public string Branch { get; set; } = "DMG";

    public bool RememberMe { get; set; } = true;
    public string? ReturnUrl { get; set; }

    // Database connection status indicator
    public bool IsDatabaseConnected { get; set; } = true;
    public string? DatabaseStatusMessage { get; set; }
    public string? DatabasePath { get; set; }
}

public sealed class DashboardViewModel
{
    public string Period { get; init; } = "Executive";
    public string PeriodLabel { get; init; } = "Executive overview";
    public IReadOnlyList<DashboardMetric> Metrics { get; init; } = [];
    public IReadOnlyList<SeriesPoint> ActivitySeries { get; init; } = [];
    public IReadOnlyList<SeriesPoint> FinancialSeries { get; init; } = [];
    public IReadOnlyList<VendorMetric> TopVendors { get; init; } = [];
    public IReadOnlyList<DeviceRecord> TraceDevices { get; init; } = [];
    public IReadOnlyList<TransactionRecord> TraceTransactions { get; init; } = [];
    public int TotalDevices { get; init; }
    public int ActiveDevices { get; init; }
    public int ExpiringSoon { get; init; }
    public int OpenReminders { get; init; }
    public int LatestBatchUnits { get; init; }
    public string LatestBatchDeviceType { get; init; } = "";
    public decimal LatestBatchCost { get; init; }
    public IReadOnlyDictionary<string, int> FleetStatusDistribution { get; init; } = new Dictionary<string, int>();
}

public sealed record DashboardMetric(string Label, string Value, string Source, string Tone, string TraceUrl);
public sealed record SeriesPoint(string Label, decimal Value, decimal SecondaryValue = 0, string? QueryTerm = null);
public sealed record VendorMetric(string Vendor, decimal Revenue, decimal Profit, int Transactions, int DevicesCount);

public sealed class DeviceFilterViewModel
{
    public string? Query { get; set; }
    public string? Vendor { get; set; }
    public string? NetworkOperator { get; set; }
    public string? Lifecycle { get; set; }
    public string? Stock { get; set; }
    public string? SpecialFilter { get; set; }
    public string? Paid { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public string? DateType { get; set; } = "All";
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalItems { get; set; }
    public int TotalPages => (PageSize >= 1000000 || PageSize <= 0) ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public IReadOnlyList<DeviceRecord> Devices { get; init; } = [];
    public IReadOnlyList<string> Vendors { get; init; } = [];
    public IReadOnlyList<string> NetworkOperators { get; init; } = [];
    public List<string> LifecycleOptions { get; set; } = new();
    public List<string> StockOptions { get; set; } = new();
}

public sealed class DeviceFormViewModel
{
    public string? Id { get; set; }
    public string? SlNo { get; set; }
    [Required, StringLength(80)] public string VehicleNo { get; set; } = "";
    [StringLength(80)] public string? DeviceName { get; set; }
    public string InstallType { get; set; } = "New";
    [StringLength(80)] public string? Type { get; set; }
    [StringLength(120)] public string? Vendor { get; set; }
    [StringLength(120)] public string? Owner { get; set; }
    [StringLength(40)] public string? CellNo { get; set; }
    [StringLength(120)] public string? Imei { get; set; }
    [StringLength(120)] public string? Iccid { get; set; }
    [StringLength(120)] public string? DeviceSerialNo { get; set; }
    [StringLength(120)] public string? SimAirtel { get; set; }
    [StringLength(120)] public string? SimBsnl { get; set; }
    [DataType(DataType.Date)] public DateTime? RegistrationDate { get; set; }
    [DataType(DataType.Date)] public DateTime? ActivationDate { get; set; }
    [DataType(DataType.Date)] public DateTime? LatestExpiryDate { get; set; }
    [StringLength(120)] public string? EngineNo { get; set; }
    [StringLength(120)] public string? ChasisNo { get; set; }
    [StringLength(120)] public string? VehicleMake { get; set; }
    [StringLength(120)] public string? VehicleModel { get; set; }
    [StringLength(120)] public string? Rto { get; set; }
    public string? Address { get; set; }
    [StringLength(120)] public string? ProofOfAddress { get; set; }
    [StringLength(120)] public string? DocumentNumber { get; set; }
    [StringLength(120)] public string? PreviousImei { get; set; }
    public string? HardwareHistory { get; set; }
    [StringLength(120)] public string? InvoiceId { get; set; }
    public string? LifecycleStatus { get; set; } = "Active";
    public string StockStatus { get; set; } = "Activated";
    public string? Notes { get; set; }
    public string ReplacementSource { get; set; } = "None";
    public string? ReplacedDeviceId { get; set; }
    public string? ReplacedDeviceSnapshot { get; set; }
    public bool ExcludeFromUnpaid { get; set; } = false;
    public bool IsMuted { get; set; } = false;
    public string? NetworkOperator { get; set; } // Optional: Taisys / Sensorise / None
    public string CdacCertificate { get; set; } = "NO";
    public string VldtCertificate { get; set; } = "NO";
    public string? Paid { get; set; }
    public decimal? AdminCost { get; set; }
}

public sealed class ReplaceVehicleRequest
{
    public string DeviceId { get; set; } = "";
    public string NewVehicleNo { get; set; } = "";
    public string? NewVendor { get; set; }
    public string? NewOwner { get; set; }
    public string? NewCellNo { get; set; }
    public string? NewEngineNo { get; set; }
    public string? NewChasisNo { get; set; }
    public string? NewProofOfAddress { get; set; }
    public string? NewDocumentNumber { get; set; }
    public string? NewAddress { get; set; }
    public string? ReplacementNotes { get; set; }
}

public sealed class TogglePaidRequest
{
    private string _deviceId = "";
    public string DeviceId 
    { 
        get => _deviceId; 
        set => _deviceId = value; 
    }
    public string Id 
    { 
        get => _deviceId; 
        set => _deviceId = value; 
    }

    private string? _targetStatus;
    public string? TargetStatus 
    { 
        get => _targetStatus; 
        set => _targetStatus = value; 
    }
    public string? Paid 
    { 
        get => _targetStatus; 
        set => _targetStatus = value; 
    }
}

public sealed class TransactionFilterViewModel
{
    public string? Query { get; set; }
    public string? PaymentMode { get; set; }
    public string? FilterVendor { get; set; }
    public string? PaymentFor { get; set; }
    public string? TransactionType { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> VendorOptions { get; set; } = new();
    public List<string> PaymentModeOptions { get; set; } = new();
    public List<string> PaymentForOptions { get; set; } = new();
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalItems { get; set; }
    public int TotalPages => (PageSize >= 1000000 || PageSize <= 0) ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public decimal TotalFilteredAmount { get; set; }
    public decimal TotalFilteredProfit { get; set; }
    public IReadOnlyList<TransactionRecord> Transactions { get; init; } = [];
}

public enum EntryMode
{
    Standard,
    BulkAllocation,
    MergePartPayments
}

public sealed class TransactionFormViewModel
{
    public string? Id { get; set; }
    [Required, DataType(DataType.Date)] public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    [Required, StringLength(80)] public string VehicleNo { get; set; } = "";
    [StringLength(80)] public string? Imei { get; set; }
    [StringLength(80)] public string? Iccid { get; set; }
    [StringLength(120)] public string Vendor { get; set; } = "";
    [StringLength(80)] public string InvoiceId { get; set; } = "";
    public string PaymentMode { get; set; } = "UPI";
    public string PaymentFor { get; set; } = "None";
    [Required(ErrorMessage = "Please enter an amount.")]
    [Range(0, 100000000)] public decimal? Amount { get; set; }
    [Range(0, 100000000)] public decimal? TotalPayment { get; set; }
    public string? BatchId { get; set; }
    public string? Utr { get; set; }
    public string? PaidTo { get; set; }
    public string? Extras { get; set; } = "None";
    public string Type { get; set; } = "New";
    public string? NetworkOperator { get; set; }
    [Range(0, 100000)] public decimal? ExtrasAmount { get; set; }
    public string? ActivationDate { get; set; }
    public string? NewExpiryDate { get; set; }
    public string? Notes { get; set; }
    public decimal? AdminCost { get; set; }
    public bool IsManualDeviceCost { get; set; } = false;
    public decimal? ManualDeviceCost { get; set; }

    public bool ApplyAdvanceCredit { get; set; } = true;
    public decimal? AdvanceCreditAmount { get; set; }
    public string? AdvanceCreditId { get; set; }
    public string? AdvanceCreditUtr { get; set; }
    public string? AdvanceCreditDate { get; set; }
    public string? AdvanceCreditMode { get; set; }
    public string? AdvanceCreditPaidTo { get; set; }
    public decimal? AvailableAdvanceBalance { get; set; }

    public EntryMode CurrentMode { get; set; } = EntryMode.Standard;
    public List<BulkAllocationRow> BulkAllocations { get; set; } = new List<BulkAllocationRow>();
    public List<MergePaymentRow> MergePayments { get; set; } = new List<MergePaymentRow>();
    public List<MergePaymentRow> BulkPayments { get; set; } = new List<MergePaymentRow>();
}

public sealed class MergePaymentRow
{
    public string? Date { get; set; }
    public string? Utr { get; set; }
    public decimal Amount { get; set; }
    public decimal TotalPayment { get; set; }
    public string PaymentMode { get; set; } = "";
    public string? PaidTo { get; set; }
}

public sealed class BulkAllocationRow
{
    public string? VehicleNo { get; set; }
    public string? Imei { get; set; }
    public string? Iccid { get; set; }
    public string? Lifecycle { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentFor { get; set; }
    public string? ActivationDate { get; set; }
    public string? NewExpiryDate { get; set; }
    public string? NetworkOperator { get; set; }
    public string? Extras { get; set; } = "None";
    public decimal ExtrasAmount { get; set; }
}

public sealed class InvoiceFormViewModel
{
    public string? Id { get; set; }
    [Required, DataType(DataType.Date)] public DateTime Date { get; set; } = DateTime.UtcNow.Date;
    public string InvoiceNo { get; set; } = "";
    public bool IsAddon { get; set; }
    public string? ExistingInvoiceNo { get; set; }
    public string DeviceType { get; set; } = "GPS";
    [Range(1, 100000)] public int Quantity { get; set; } = 1;
    [Range(0, 100000000)] public decimal? Rate { get; set; }
    [Range(0, 100000000)] public decimal? CourierCharges { get; set; }
    public string? Notes { get; set; }
    
    [Range(0, 100000)] public int? PanicButtonQuantity { get; set; }
    [Range(0, 100000000)] public decimal? PanicButtonCost { get; set; }

    public string? AckNo { get; set; }
    public string? EwayBillNumber { get; set; }
    public int LinkedDevicesCount { get; set; }
    public string NetworkOperator { get; set; } = ""; // Taisys / Sensorise
}

public sealed class MultipleInvoiceFormViewModel
{
    public string InvoiceNo { get; set; } = "";
    public bool IsAddon { get; set; }
    public string? ExistingInvoiceNo { get; set; }

    [Required, DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    public Microsoft.AspNetCore.Http.IFormFile? File { get; set; }

    [StringLength(80)] public string DeviceType { get; set; } = "GPS";
    [Range(1, 100000)] public int Quantity { get; set; } = 1;
    [Range(0, 1000000)] public decimal? Rate { get; set; }
    [Range(0, 1000000)] public decimal? CourierCharges { get; set; }
    public string? Notes { get; set; }

    [Range(0, 100000)] public int? PanicButtonQuantity { get; set; }
    [Range(0, 1000000)] public decimal? PanicButtonCost { get; set; }

    public string StockStatus { get; set; } = "New";
    public string? NetworkOperator { get; set; }
}

public sealed class MultipleInvoiceResultViewModel
{
    public InvoiceRecord Invoice { get; set; } = null!;
    public List<DeviceRecord> Devices { get; set; } = [];
    public string StockStatus { get; set; } = "New";
}

public sealed class InvoiceIndexItemViewModel
{
    public InvoiceRecord Invoice { get; set; } = null!;
    public List<DeviceRecord> Devices { get; set; } = [];
}
public sealed class InvoiceListViewModel
{
    public string? Query { get; set; }
    public string? NetworkOperator { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public int? Quantity { get; set; }
    public IReadOnlyList<string> NetworkOperators { get; set; } = [];
    public IReadOnlyList<int> Quantities { get; set; } = [];
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalItems { get; set; }
    public int TotalPages => (PageSize >= 1000000 || PageSize <= 0) ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public decimal TotalValue { get; set; }
    public int TotalUnits { get; set; }
    public decimal TotalExtras { get; set; }
    public decimal TotalBaseAmount { get; set; }
    public IReadOnlyList<InvoiceIndexItemViewModel> Invoices { get; init; } = [];
}

public sealed class InvoiceCreatePageViewModel
{
    public InvoiceFormViewModel SingleModel { get; set; } = new();
    public MultipleInvoiceFormViewModel MultipleModel { get; set; } = new();
    public string ActiveTab { get; set; } = "single";
    public List<string> ExistingInvoices { get; set; } = [];
    public string? SelectedAddonBatch { get; set; }
}



public sealed class ReminderViewModel
{
    public string? Id { get; set; }
    [Required, StringLength(120)] public string Title { get; set; } = "";
    [StringLength(500)] public string Details { get; set; } = "";
    [DataType(DataType.Date)] public DateTime? DueDate { get; set; } = DateTime.UtcNow.Date;
    public string TargetAudience { get; set; } = "@all";
}

public sealed class ReminderPageViewModel
{
    public IReadOnlyDictionary<string, IReadOnlyList<DeviceRecord>> Buckets { get; init; } = new Dictionary<string, IReadOnlyList<DeviceRecord>>();
    public IReadOnlyList<ManualReminder> ManualReminders { get; init; } = [];
    public IReadOnlyList<RechargeRecord> PendingRecharges { get; init; } = [];
    public IReadOnlyList<RechargeRecord> UnpaidRecharges { get; init; } = [];
}

public sealed class ConfigViewModel
{
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsEditingEnabled { get; set; } = false;
}

public static class AppModules
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Dashboard",
        "Devices",
        "Inventory",
        "Transactions",
        "Invoices",
        "Reminders",
        "DataShare",
        "Recharges",
        "CompanyRecharges",
        "Vendors"
    };
}

public sealed class CompanyRechargesIndexViewModel
{
    public string? Query { get; set; }
    public string? SearchQuery { get => Query; set => Query = value; }
    public string? FilterProvider { get; set; }
    public string? ProviderFilter { get => FilterProvider; set => FilterProvider = value; }
    public decimal TotalAdvanceOverall { get; set; }
    public decimal TotalAdvancePaid { get => TotalAdvanceOverall; set => TotalAdvanceOverall = value; }
    public decimal TotalConsumedOverall { get; set; }
    public decimal TotalConsumed { get => TotalConsumedOverall; set => TotalConsumedOverall = value; }
    public decimal TotalBalanceOverall { get; set; }
    public decimal TotalRemainingBalance { get => TotalBalanceOverall; set => TotalBalanceOverall = value; }
    public int TotalBatchesCount { get; set; }
    public int TotalBatches { get => TotalBatchesCount; set => TotalBatchesCount = value; }
    public int TotalDevicesCount { get; set; }
    public int TotalDevicesProcessed { get => TotalDevicesCount; set => TotalDevicesCount = value; }
    public List<CompanyRechargeBatch> Batches { get; set; } = new();
    public List<CompanyRechargeBatch> AvailableSourceBatchesForTransfer { get; set; } = new();
    public decimal DefaultRechargeRate { get; set; } = 1298m;
    public decimal DefaultReactivationRate { get; set; } = 1534m;

    // BlueMine / MYGTS split KPIs
    public decimal BluemineRechargeRate { get; set; } = 1156.4m;
    public decimal BluemineReactivationRate { get; set; } = 1392.4m;
    public decimal BluemineAdvance { get; set; }
    public decimal BluemineConsumed { get; set; }
    public decimal BluemineBalance => BluemineAdvance - BluemineConsumed;
    public int BluemineBatchCount { get; set; }
    public int BluemineDevicesCount { get; set; }

    public decimal MygtsAdvance { get; set; }
    public decimal MygtsConsumed { get; set; }
    public decimal MygtsBalance => MygtsAdvance - MygtsConsumed;
    public int MygtsBatchCount { get; set; }
    public int MygtsDevicesCount { get; set; }
}

public sealed class CompanyRechargeBatchFormViewModel
{
    public string? Id { get; set; }
    public string PoNumber { get; set; } = "";
    public string PayReferenceNo { get; set; } = "";
    public string PaymentReferenceNo { get => PayReferenceNo; set => PayReferenceNo = value; }
    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    public string BatchDate { get => Date; set => Date = value; }
    public string Provider { get; set; } = "Taisys";
    public decimal AdvanceAmount { get; set; } = 0m;
    public decimal CarriedForwardAmount { get; set; } = 0m;
    public decimal CarriedOutAmount { get; set; } = 0m;
    public string? TransferredFromBatchId { get; set; }
    public string? TransferredFromBatchPo { get; set; }
    public string? TransferredToBatchId { get; set; }
    public string? TransferredToBatchPo { get; set; }
    public string Notes { get; set; } = "";
    public decimal DefaultRechargeRate { get; set; } = 1298m;
    public decimal DefaultReactivationRate { get; set; } = 1534m;
    public List<CompanyRechargeItemInputModel> Items { get; set; } = new();
}

public sealed class CompanyRechargeItemInputModel
{
    public string? Id { get; set; }
    public int SlNo { get; set; } = 1;
    public string VehicleNo { get; set; } = "";
    public string Imei { get; set; } = "";
    public string Iccid { get; set; } = "";
    public string RequestId { get; set; } = "-";
    public string ActionType { get; set; } = "Renewal";
    public decimal PerDeviceAmount { get; set; } = 1298m;
}

public sealed class CompanyRechargeAddDevicesViewModel
{
    public string BatchId { get; set; } = "";
    public string PoNumber { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Date { get; set; } = "";
    public decimal AdvanceAmount { get; set; }
    public decimal CarriedForwardAmount { get; set; }
    public decimal CarriedOutAmount { get; set; }
    public decimal TotalAmountExisting { get; set; }
    public decimal RemainingBalance { get; set; }
    public int ExistingDeviceCount { get; set; }
    public decimal DefaultRechargeRate { get; set; } = 1298m;
    public decimal DefaultReactivationRate { get; set; } = 1534m;
    public List<CompanyRechargeItemInputModel> Items { get; set; } = new();
}

public sealed class UserFormViewModel
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, StringLength(120, MinimumLength = 8), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, StringLength(80)] public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "User";
    public List<string> Branches { get; set; } = new() { "DMG", "RTO", "Expenditure" };
    public Dictionary<string, string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UserEditFormViewModel
{
    [Required] public string Id { get; set; } = "";
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, StringLength(80)] public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "User";
    public List<string> Branches { get; set; } = new() { "DMG", "RTO", "Expenditure" };
    [StringLength(120, MinimumLength = 8), DataType(DataType.Password)] public string? Password { get; set; }
    public string PlainPassword { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsProtectedAdmin { get; set; } = false;
    public string WhatsAppPreference { get; set; } = "Ask"; // "Ask", "Web", "App"
    public Dictionary<string, string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> AllowedRoles { get; set; } = new();
}

public sealed class WhatsAppPreferenceModel
{
    public string Preference { get; set; } = "Ask";
}

public sealed class UserListViewModel
{
    public IReadOnlyList<UserRecord> Users { get; init; } = [];
    public bool IsAdmin { get; init; }
    public string CurrentUserEmail { get; init; } = "";
    public string CurrentUserRole { get; init; } = "";
    public List<string> AllowedRoles { get; init; } = new();
}

public sealed class ImportViewModel
{
    [Required] public IFormFile? Workbook { get; set; }
    public bool ExcludeFromUnpaid { get; set; } = false;
    public bool IsMuted { get; set; } = false;
}

public sealed class DeviceImportReviewViewModel
{
    public string Branch { get; set; } = "DMG";
    public int TotalRows { get; set; }
    public int NewCount { get; set; }
    public int ConflictCount { get; set; }
    public List<DeviceImportReviewItemViewModel> Items { get; set; } = [];
    public string StagedDevicesJson { get; set; } = "";
    public bool ExcludeFromUnpaid { get; set; } = false;
    public bool IsMuted { get; set; } = false;
}

public sealed class DeviceImportReviewItemViewModel
{
    public int Index { get; set; }
    public DeviceRecord Imported { get; set; } = new();
    public DeviceRecord? Existing { get; set; }
    public bool IsConflict { get; set; }
    public string Action { get; set; } = "insert";
}

public sealed class DeviceImportConfirmSubmitModel
{
    public string Branch { get; set; } = "DMG";
    public string StagedDevicesJson { get; set; } = "";
    public Dictionary<int, string> Actions { get; set; } = new();
    public bool ExcludeFromUnpaid { get; set; } = false;
    public bool IsMuted { get; set; } = false;
}

public sealed class TransactionImportReviewViewModel
{
    public string Branch { get; set; } = "DMG";
    public int TotalRows { get; set; }
    public int NewCount { get; set; }
    public int ConflictCount { get; set; }
    public List<TransactionImportReviewItemViewModel> Items { get; set; } = [];
    public string StagedTransactionsJson { get; set; } = "";
}

public sealed class TransactionImportReviewItemViewModel
{
    public int Index { get; set; }
    public TransactionRecord Imported { get; set; } = new();
    public TransactionRecord? Existing { get; set; }
    public bool IsConflict { get; set; }
    public string Action { get; set; } = "insert";
}

public sealed class TransactionImportConfirmSubmitModel
{
    public string Branch { get; set; } = "DMG";
    public string StagedTransactionsJson { get; set; } = "";
    public Dictionary<int, string> Actions { get; set; } = new();
}

public sealed class DeviceImportSubmitModel
{
    public string Branch { get; set; } = "DMG";
    public List<DeviceImportRowSubmitItem> Rows { get; set; } = [];
}

public sealed class DeviceImportRowSubmitItem
{
    public string? ExistingId { get; set; }
    public string Action { get; set; } = "insert";
    public string SlNo { get; set; } = "";
    public string VehicleNo { get; set; } = "";
    public string Iccid { get; set; } = "";
    public string Imei { get; set; } = "";
    public string SimAirtel { get; set; } = "";
    public string SimBsnl { get; set; } = "";
    public string Status { get; set; } = "Active";
    public string Type { get; set; } = "GPS";
    public string DeviceName { get; set; } = "";
    public string InvoiceId { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Owner { get; set; } = "";
    public string CellNo { get; set; } = "";
    public string RegistrationDate { get; set; } = "";
    public string ActivationDate { get; set; } = "";
    public string LatestExpiryDate { get; set; } = "";
    public string EngineNo { get; set; } = "";
    public string ChasisNo { get; set; } = "";
    public string ProofOfAddress { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Notes { get; set; } = "";
    public string LifecycleStatus { get; set; } = "Active";
    public string StockStatus { get; set; } = "Used";
}

public sealed class BulkSplitViewModel
{
    [Required, StringLength(80)] public string BatchId { get; set; } = "";
    [Range(0.01, 100000000)] public decimal TotalPayment { get; set; }
    public List<BulkAllocationRow> BulkAllocations { get; set; } = new();
    public List<MergePaymentRow> BulkPayments { get; set; } = new();
    public string PaymentMode { get; set; } = "Bank transfer";
    public string Vendor { get; set; } = "";
    public string PaymentFor { get; set; } = "None";
    public string Extras { get; set; } = "None";
    public string? Notes { get; set; }
    public string? Utr { get; set; }
    public string? PaidTo { get; set; }
    [DataType(DataType.Date)] public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    public bool ApplyAdvanceCredit { get; set; } = true;
    public decimal? AdvanceCreditAmount { get; set; }
    public string? AdvanceCreditId { get; set; }
    public string? AdvanceCreditUtr { get; set; }
    public string? AdvanceCreditDate { get; set; }
    public string? AdvanceCreditMode { get; set; }
    public string? AdvanceCreditPaidTo { get; set; }
    public decimal? AvailableAdvanceBalance { get; set; }
}

public sealed class InventoryViewModel
{
    public string? Query { get; set; }
    public string? SortColumn { get; set; }
    public string? SortDirection { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalItems { get; set; }
    public int TotalPages => (PageSize >= 1000000 || PageSize <= 0) ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public IReadOnlyList<InvoiceGroup> Batches { get; init; } = [];
}

public sealed class InvoiceGroup
{
    public string InvoiceNo { get; init; } = "";
    public string InvoiceId { get; init; } = "";
    public string DeviceType { get; init; } = "";
    public string NetworkOperator { get; init; } = "";
    public string Date { get; init; } = "";
    public IReadOnlyList<DeviceRecord> Devices { get; init; } = [];
}

// ===== Recharge List ViewModels =====

public sealed class RechargeFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Vehicle number is required.")]
    public string VehicleNo { get; set; } = "";

    [Required(ErrorMessage = "IMEI is required.")]
    public string Imei { get; set; } = "";

    [Required(ErrorMessage = "ICCID is required.")]
    public string Iccid { get; set; } = "";

    [Required(ErrorMessage = "SIM Airtel number is required.")]
    public string SimAirtel { get; set; } = "";

    [Required(ErrorMessage = "SIM BSNL number is required.")]
    public string SimBsnl { get; set; } = "";

    [Required(ErrorMessage = "Vendor is required.")]
    public string Vendor { get; set; } = "";

    [Required(ErrorMessage = "Network operator is required.")]
    public string NetworkOperator { get; set; } = "";

    public string PaymentStatus { get; set; } = "Not Paid";
    public string RechargeStatus { get; set; } = "Pending";

    [Required(ErrorMessage = "Recharge date is required.")]
    public string RechargeDate { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    public string ExpiryDate { get; set; } = "";

    [Required(ErrorMessage = "Previous expiry date is required.")]
    public string PreviousExpiryDate { get; set; } = "";

    public string ActivationDate { get; set; } = "";

    public decimal AmountPaid { get; set; } = 0;

    public string Notes { get; set; } = "";
}

public sealed class RechargeListViewModel
{
    public IReadOnlyList<RechargeRecord> Recharges { get; init; } = [];
    public IReadOnlyList<RechargeAnalyticsItem> OutstandingPayments { get; init; } = [];
    public int TotalProcessed { get; init; }
    public int TotalPending { get; init; }
    public int TotalPaid { get; init; }
    public int TotalNotPaid { get; init; }
    public int OutstandingCount { get; init; } // Processed but no matching transaction
    public int PendingTodayCount { get; init; }
}

public sealed class RechargeAnalyticsItem
{
    public string VehicleNo { get; init; } = "";
    public string RechargeDate { get; init; } = "";
    public string ActivationDate { get; init; } = "";
    public string NetworkOperator { get; init; } = "";
    public string PaymentStatus { get; init; } = "";
    public string RechargeStatus { get; init; } = "";
}

// ===== Data Share ViewModel =====

public sealed class DataShareViewModel
{
    public string? Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalItems { get; set; }
    public int TotalPages => (PageSize >= 1000000 || PageSize <= 0) ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public IReadOnlyList<DeviceRecord> Results { get; init; } = [];
}

public sealed class DataShareHistoryViewModel
{
    public string? Query { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalItems { get; set; }
    public int TotalPages => (PageSize >= 1000000 || PageSize <= 0) ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    public IReadOnlyList<DataShareRecord> Records { get; init; } = [];
    public int TotalUniqueVehicles { get; set; }
    public int TotalSharesToday { get; set; }
    public int ActiveSendersCount { get; set; }
}

public sealed class DataShareLogRequest
{
    public string VehicleNo { get; set; } = "";
    public string Imei { get; set; } = "";
    public string Iccid { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DeviceBranch { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class SensoriseDeviceItem
{
    public DeviceRecord Device { get; set; } = new();
    public string LifecycleStatus { get; set; } = "";
    public int DaysPastExpiry { get; set; }
    public bool IsRecentlyDead { get; set; }
    public int DeadDaysRemaining { get; set; }
    public string StatusBadgeClass { get; set; } = "";
}

public sealed class SensoriseViewModel
{
    public string? Query { get; set; }
    public string StatusFilter { get; set; } = "All"; // "All", "Suspension", "Terminated", "Dead"
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalItems { get; set; }
    public int TotalPages => (PageSize >= 1000000 || PageSize <= 0) ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
    
    public int SuspensionCount { get; set; }
    public int TerminatedCount { get; set; }
    public int SuspensionOrTerminationCount => SuspensionCount + TerminatedCount;
    public int RecentlyDeadCount { get; set; }
    public int TotalSensoriseActiveCount => SuspensionCount + TerminatedCount + RecentlyDeadCount;

    public IReadOnlyList<SensoriseDeviceItem> Devices { get; set; } = [];
}


