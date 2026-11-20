using System.ComponentModel.DataAnnotations;

namespace MygtsMvc.Models;

public class ExpenditureRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    [Required]
    [Display(Name = "RECORD DATE")]
    public string RecordDate { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");
    
    [Required]
    [Display(Name = "DOCUMENT TYPE")]
    public string DocumentType { get; set; } = "PI"; // 'PI' or 'INVOICE'
    
    [Required]
    [Display(Name = "REFERENCE NUMBER")]
    public string ReferenceNumber { get; set; } = string.Empty;
    
    [Required]
    [Display(Name = "COMPANY")]
    public string Company { get; set; } = string.Empty;
    
    [Required]
    [Display(Name = "FOR")]
    public string Purpose { get; set; } = string.Empty; // 'For' column
    
    [Required]
    [Display(Name = "BANK")]
    public string Bank { get; set; } = string.Empty;
    
    [Required]
    [Display(Name = "PAYMENT TYPE")]
    public string PaymentType { get; set; } = string.Empty;
    
    [Required]
    [Display(Name = "CASH FLOW")]
    public string CashFlow { get; set; } = "CREDITED"; // 'CREDITED' or 'DEBITED'
    
    [Display(Name = "UTR / REFERENCE")]
    public string? Utr { get; set; }
    
    [Required]
    [Range(0.01, 999999999.99, ErrorMessage = "PLEASE ENTER A VALID AMOUNT.")]
    [Display(Name = "AMOUNT (₹)")]
    public decimal Amount { get; set; }
    
    [Display(Name = "PAYMENT DATE")]
    public string? PaymentDate { get; set; }
    
    [Display(Name = "UNPAID (YET TO PAY)")]
    public bool IsUnpaid { get; set; } = false;
    
    public bool IsConvertedToInvoice { get; set; }
    
    public string? OriginalPiNumber { get; set; }
    
    public string? OriginalPiDate { get; set; }
    
    [Display(Name = "INVOICE NUMBER")]
    public string? InvoiceNumber { get; set; }
    
    [Display(Name = "INVOICE DATE")]
    public string? InvoiceDate { get; set; }
    
    [Display(Name = "NOTES")]
    public string? Notes { get; set; }
    
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
    
    public string? CreatedBy { get; set; }

    public string? SourceBranch { get; set; } = "Expenditure";
    public bool IsFromTransaction { get; set; } = false;

    /// <summary>
    /// Displays effective active reference number (Invoice # if converted, otherwise PI #)
    /// </summary>
    public string DisplayReferenceNumber =>
        IsConvertedToInvoice && !string.IsNullOrWhiteSpace(InvoiceNumber)
            ? InvoiceNumber
            : ReferenceNumber;

    /// <summary>
    /// Displays effective active date (Invoice Date if converted, otherwise RecordDate)
    /// </summary>
    public string DisplayDate =>
        IsConvertedToInvoice && !string.IsNullOrWhiteSpace(InvoiceDate)
            ? InvoiceDate
            : RecordDate;

    /// <summary>
    /// Displays effective active type ('INVOICE' if converted, otherwise DocumentType)
    /// </summary>
    public string DisplayDocumentType =>
        IsConvertedToInvoice ? "INVOICE" : DocumentType.ToUpperInvariant();
}

public class DropdownOptionRecord
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty; // 'Bank', 'PaymentType'
    public string Value { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class ExpenditureUserRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    public string DisplayName { get; set; } = string.Empty;
    
    [Required]
    public string PlainPassword { get; set; } = string.Empty;
    
    public string PasswordHash { get; set; } = string.Empty;
    
    [Required]
    public string Role { get; set; } = "USER"; // 'ADMIN' or 'USER'
    
    public bool IsActive { get; set; } = true;
    
    public string? ActiveSessionToken { get; set; }
    
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
}

public static class FinancialYearHelper
{
    /// <summary>
    /// In India, FY runs from April 1 (Month 4) to March 31.
    /// E.g. November 2026 -> Start Year 2026 (FY 2026-27).
    /// February 2026 -> Start Year 2025 (FY 2025-26).
    /// </summary>
    public static int GetFinancialYearStart(DateTime date)
    {
        return date.Month >= 4 ? date.Year : date.Year - 1;
    }

    public static int GetFinancialYearStart(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return CurrentFinancialYearStart;
        var clean = dateStr.Trim();
        if (clean.StartsWith("0226")) clean = "2026" + clean.Substring(4);
        if (DateTime.TryParse(clean, out var d))
        {
            return GetFinancialYearStart(d);
        }
        return CurrentFinancialYearStart;
    }

    public static int CurrentFinancialYearStart => GetFinancialYearStart(DateTime.Now);

    public static string FormatLabel(int startYear)
    {
        if (startYear <= 0) return "ALL FINANCIAL YEARS";
        var endShort = (startYear + 1) % 100;
        return $"FY {startYear}-{endShort:D2}";
    }

    public static (string StartDate, string EndDate) GetDateRange(int startYear)
    {
        return ($"{startYear}-04-01", $"{startYear + 1}-03-31");
    }

    public static bool BelongsToFinancialYear(string? dateStr, int startYear)
    {
        if (startYear <= 0) return true;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;
        var clean = dateStr.Trim();
        if (clean.StartsWith("0226")) clean = "2026" + clean.Substring(4);
        if (DateTime.TryParse(clean, out var d))
        {
            return GetFinancialYearStart(d) == startYear;
        }
        var (s, e) = GetDateRange(startYear);
        return string.Compare(clean, s, StringComparison.OrdinalIgnoreCase) >= 0 &&
               string.Compare(clean, e, StringComparison.OrdinalIgnoreCase) <= 0;
    }
}

public class ExpenditureFilterViewModel
{
    public string? Query { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? DocumentType { get; set; } // 'PI', 'INVOICE'
    public string? Bank { get; set; }
    public string? PaymentType { get; set; }
    public string? Company { get; set; }
    public string? CashFlow { get; set; } // 'CREDITED', 'DEBITED'
    public string? Branch { get; set; } // 'ALL', 'DMG', 'RTO', 'EXPENDITURE'
    public int? FinancialYear { get; set; }
    public bool IsPreviousYears { get; set; } = false;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class ExpenditureListViewModel
{
    public List<ExpenditureRecord> Records { get; set; } = new();
    public decimal TotalCredited { get; set; }
    public decimal TotalDebited { get; set; }
    public decimal DmgInflow { get; set; }
    public decimal RtoInflow { get; set; }
    public decimal DirectExpInflow { get; set; }
    public decimal TotalBranchInflow => DmgInflow + RtoInflow;
    public decimal CombinedInflow => DirectExpInflow + TotalBranchInflow;
    public decimal NetCashFlow => CombinedInflow - TotalDebited;
    public decimal TotalTransactionProfit { get; set; }
    public int PendingPiCount { get; set; }
    public decimal PendingPiAmount { get; set; }
    public int InvoiceCount { get; set; }
    public decimal InvoiceAmount { get; set; }
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / (PageSize > 0 ? PageSize : 25));
    public ExpenditureFilterViewModel Filters { get; set; } = new();
    public List<string> AvailableBanks { get; set; } = new();
    public List<string> AvailablePaymentTypes { get; set; } = new();
    public List<string> AvailableCompanies { get; set; } = new();
    public int SelectedFinancialYear { get; set; } = FinancialYearHelper.CurrentFinancialYearStart;
    public string FinancialYearLabel => FinancialYearHelper.FormatLabel(SelectedFinancialYear);
    public bool IsCurrentFinancialYear => SelectedFinancialYear == FinancialYearHelper.CurrentFinancialYearStart;
}

public class ExpenditureFormViewModel
{
    public ExpenditureRecord Record { get; set; } = new();
    public List<string> AvailableBanks { get; set; } = new();
    public List<string> AvailablePaymentTypes { get; set; } = new();
    public bool ConvertToInvoice { get; set; }
}

public class ExpenditureBranchTransactionsFilterViewModel
{
    public string? Query { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? Branch { get; set; } = "ALL"; // 'ALL', 'DMG', 'RTO'
    public string? PaidTo { get; set; }
    public string? PaymentMode { get; set; }
    public string? Vendor { get; set; }
    public int? FinancialYear { get; set; }
    public bool IsPreviousYears { get; set; } = false;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class ExpenditureBranchTransactionsViewModel
{
    public List<TransactionRecord> Transactions { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal DmgAmount { get; set; }
    public int DmgCount { get; set; }
    public decimal RtoAmount { get; set; }
    public int RtoCount { get; set; }
    public int TotalCount { get; set; }
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / (PageSize > 0 ? PageSize : 25));
    public ExpenditureBranchTransactionsFilterViewModel Filters { get; set; } = new();
    public List<string> AvailableBanks { get; set; } = new();
    public List<string> AvailablePaymentModes { get; set; } = new();
    public List<string> AvailableVendors { get; set; } = new();
    public int SelectedFinancialYear { get; set; } = FinancialYearHelper.CurrentFinancialYearStart;
    public string FinancialYearLabel => FinancialYearHelper.FormatLabel(SelectedFinancialYear);
    public bool IsCurrentFinancialYear => SelectedFinancialYear == FinancialYearHelper.CurrentFinancialYearStart;
}

public class FinancialYearSummaryItem
{
    public int StartYear { get; set; }
    public string Label => FinancialYearHelper.FormatLabel(StartYear);
    public string DateRangeText => $"{StartYear}-04-01 to {StartYear + 1}-03-31";
    public decimal DirectInflow { get; set; }
    public decimal BranchInflow { get; set; }
    public decimal TotalInflow => DirectInflow + BranchInflow;
    public decimal TotalOutflow { get; set; }
    public decimal NetCashFlow => TotalInflow - TotalOutflow;
    public int InvoiceCount { get; set; }
    public int PiCount { get; set; }
    public int TransactionCount { get; set; }
    public bool IsCurrentYear => StartYear == FinancialYearHelper.CurrentFinancialYearStart;
}

public class ExpenditurePreviousYearsViewModel
{
    public int SelectedYear { get; set; } // 0 for ALL Financial Years, or specific year e.g. 2026
    public string SelectedYearLabel => SelectedYear == 0 ? "ALL FINANCIAL YEARS" : FinancialYearHelper.FormatLabel(SelectedYear);
    public List<FinancialYearSummaryItem> YearlySummaries { get; set; } = new();
    public List<int> AvailablePreviousYears { get; set; } = new();
    public ExpenditureListViewModel ExpensesModel { get; set; } = new();
    public ExpenditureBranchTransactionsViewModel TransactionsModel { get; set; } = new();
    public string ActiveTab { get; set; } = "EXPENSES"; // 'EXPENSES' or 'TRANSACTIONS' or 'OVERVIEW'
    public decimal OverallPreviousInflow { get; set; }
    public decimal OverallPreviousOutflow { get; set; }
    public decimal OverallPreviousNetCashFlow => OverallPreviousInflow - OverallPreviousOutflow;
}

public class ExpenditureCreditRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required(ErrorMessage = "COMPANY / PARTY NAME IS REQUIRED.")]
    [Display(Name = "COMPANY / PARTY NAME")]
    public string CompanyName { get; set; } = string.Empty;

    [Required(ErrorMessage = "CREDIT AMOUNT IS REQUIRED.")]
    [Range(0.01, 999999999.99, ErrorMessage = "PLEASE ENTER A VALID CREDIT AMOUNT.")]
    [Display(Name = "CREDIT AMOUNT (₹)")]
    public decimal CreditAmount { get; set; }

    /// <summary>
    /// Credit Direction:
    /// 'GIVEN_TO_US' = Credit given to us (Creditor / Liability - We owe them)
    /// 'GIVEN_BY_US' = Credit given by us (Debtor / Asset - They owe us)
    /// </summary>
    [Display(Name = "CREDIT DIRECTION")]
    public string CreditType { get; set; } = "GIVEN_TO_US";

    /// <summary>
    /// Whether this credit line has been settled / paid off
    /// </summary>
    [Display(Name = "PAID OFF / SETTLED")]
    public bool IsPaidOff { get; set; } = false;

    [Display(Name = "PAID OFF DATE")]
    public string? PaidOffDate { get; set; }

    [Display(Name = "DATE")]
    public string Date { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [Display(Name = "DESCRIPTION / PURPOSE")]
    public string? Description { get; set; }

    [Display(Name = "NOTES / REMARKS")]
    public string? Notes { get; set; }

    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

    public string? CreatedBy { get; set; }

    public bool IsGivenToUs => string.Equals(CreditType, "GIVEN_TO_US", StringComparison.OrdinalIgnoreCase);
    public string DisplayTypeLabel => IsGivenToUs ? "GIVEN TO US" : "GIVEN BY US";
}

public class ExpenditureCreditsViewModel
{
    public List<ExpenditureCreditRecord> Credits { get; set; } = new();

    // Overall totals
    public decimal TotalCreditAmount { get; set; }
    public int TotalCompaniesCount { get; set; }
    public int TotalCreditLines { get; set; }
    public decimal AverageCreditPerCompany => TotalCompaniesCount > 0 ? TotalCreditAmount / TotalCompaniesCount : 0;

    // Given To Us metrics (Liability / We Owe)
    public decimal TotalGivenToUs { get; set; }
    public decimal OutstandingGivenToUs { get; set; }
    public decimal PaidOffGivenToUs { get; set; }
    public int CountGivenToUs { get; set; }

    // Given By Us metrics (Asset / They Owe Us)
    public decimal TotalGivenByUs { get; set; }
    public decimal OutstandingGivenByUs { get; set; }
    public decimal PaidOffGivenByUs { get; set; }
    public int CountGivenByUs { get; set; }

    // Overall Settlement metrics
    public decimal TotalPaidOffAmount { get; set; }
    public decimal TotalOutstandingAmount { get; set; }
    public int PaidOffCount { get; set; }
    public int OutstandingCount { get; set; }

    public ExpenditureCreditRecord FormModel { get; set; } = new();
    public string? SearchQuery { get; set; }
    public string? StatusFilter { get; set; } = "ALL";
    public string? DirectionFilter { get; set; } = "ALL";
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public decimal FilteredTotalAmount => Credits.Sum(c => c.CreditAmount);
    public decimal FilteredOutstandingAmount => Credits.Where(c => !c.IsPaidOff).Sum(c => c.CreditAmount);
    public decimal FilteredPaidOffAmount => Credits.Where(c => c.IsPaidOff).Sum(c => c.CreditAmount);
}


