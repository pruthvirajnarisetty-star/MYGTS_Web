using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;
using MygtsMvc.Models;

namespace MygtsMvc.Services;

public interface IExpenditureDataStore
{
    string DatabasePath { get; }
    Task<ExpenditureListViewModel> GetExpendituresAsync(ExpenditureFilterViewModel filters);
    Task<ExpenditureRecord?> GetExpenditureByIdAsync(string id);
    Task CreateExpenditureAsync(ExpenditureRecord record);
    Task UpdateExpenditureAsync(ExpenditureRecord record);
    Task DeleteExpenditureAsync(string id);
    Task<List<string>> GetDropdownOptionsAsync(string category);
    Task<bool> AddDropdownOptionAsync(string category, string value);
    Task<List<string>> GetAllCompaniesAsync();
    
    // User Management for Expenditure
    Task<ExpenditureUserRecord?> GetUserByEmailAsync(string email);
    Task<List<ExpenditureUserRecord>> GetAllUsersAsync();
    Task CreateUserAsync(ExpenditureUserRecord user);
    Task UpdateUserAsync(ExpenditureUserRecord user);
    Task DeleteUserAsync(string id);
    Task UpdateUserSessionTokenAsync(string id, string token);
    
    // Export
    Task<byte[]> ExportToCsvAsync(ExpenditureFilterViewModel filters);

    // Branch Transactions (DMG & RTO)
    Task<ExpenditureBranchTransactionsViewModel> GetBranchTransactionsAsync(ExpenditureBranchTransactionsFilterViewModel filters);
    Task<byte[]> ExportBranchTransactionsCsvAsync(ExpenditureBranchTransactionsFilterViewModel filters);

    // Previous Financial Years
    Task<ExpenditurePreviousYearsViewModel> GetPreviousYearsDataAsync(int selectedYear, string activeTab, ExpenditureFilterViewModel expFilters, ExpenditureBranchTransactionsFilterViewModel txnFilters);

    // Company Credit Records
    Task<List<ExpenditureCreditRecord>> GetAllCreditsAsync();
    Task<ExpenditureCreditRecord?> GetCreditByIdAsync(string id);
    Task CreateCreditAsync(ExpenditureCreditRecord credit);
    Task UpdateCreditAsync(ExpenditureCreditRecord credit);
    Task DeleteCreditAsync(string id);
    Task ToggleCreditPaidOffAsync(string id);
}

public class ExpenditureDataStore : IExpenditureDataStore
{
    private readonly IAppDataStore _appStore;
    private readonly string _connectionString;
    private readonly string _mainDbPath;
    private readonly string _dbPath;

    public string DatabasePath => _dbPath;

    public ExpenditureDataStore(IWebHostEnvironment env, IAppDataStore appStore)
    {
        _appStore = appStore;
        var commonFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyGts_database");
        Directory.CreateDirectory(commonFolder);
        
        var primaryPath = Path.Combine(commonFolder, "Expenditure_MYGTS.db");
        var templatePath = Path.Combine(env.ContentRootPath, "Data", "Expenditure_MYGTS.db");

        if (!File.Exists(primaryPath) && File.Exists(templatePath))
        {
            File.Copy(templatePath, primaryPath, true);
        }

        _dbPath = File.Exists(primaryPath) ? primaryPath : templatePath;
        _connectionString = $"Data Source={_dbPath}";
        _mainDbPath = Path.Combine(commonFolder, "mygts_database.db");
        
        EnsureTablesCreated();
    }

    private SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    private void EnsureTablesCreated()
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Expenditures (
            Id TEXT PRIMARY KEY,
            RecordDate TEXT NOT NULL,
            DocumentType TEXT NOT NULL,
            ReferenceNumber TEXT NOT NULL,
            Company TEXT NOT NULL,
            Purpose TEXT NOT NULL,
            Bank TEXT NOT NULL,
            PaymentType TEXT NOT NULL,
            CashFlow TEXT NOT NULL,
            Utr TEXT,
            Amount REAL NOT NULL DEFAULT 0.0,
            PaymentDate TEXT,
            IsConvertedToInvoice INTEGER NOT NULL DEFAULT 0,
            OriginalPiNumber TEXT,
            OriginalPiDate TEXT,
            InvoiceNumber TEXT,
            InvoiceDate TEXT,
            Notes TEXT,
            CreatedAt TEXT NOT NULL,
            CreatedBy TEXT
        );

        CREATE TABLE IF NOT EXISTS DropdownOptions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Category TEXT NOT NULL,
            Value TEXT NOT NULL,
            IsActive INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS Users (
            Id TEXT PRIMARY KEY,
            Email TEXT NOT NULL UNIQUE,
            DisplayName TEXT NOT NULL,
            PlainPassword TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            Role TEXT NOT NULL,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ExpenditureCredits (
            Id TEXT PRIMARY KEY,
            CompanyName TEXT NOT NULL,
            CreditAmount REAL NOT NULL DEFAULT 0.0,
            CreditType TEXT NOT NULL DEFAULT 'GIVEN_TO_US',
            IsPaidOff INTEGER NOT NULL DEFAULT 0,
            PaidOffDate TEXT,
            Date TEXT,
            Description TEXT,
            Notes TEXT,
            CreatedAt TEXT NOT NULL,
            CreatedBy TEXT
        );
        ";
        cmd.ExecuteNonQuery();

        // Ensure CreditType, IsPaidOff, PaidOffDate columns exist in ExpenditureCredits table
        using (var checkCreditsCol = conn.CreateCommand())
        {
            checkCreditsCol.CommandText = "PRAGMA table_info(ExpenditureCredits);";
            bool hasCreditType = false;
            bool hasIsPaidOff = false;
            bool hasPaidOffDate = false;
            using var colReader = checkCreditsCol.ExecuteReader();
            while (colReader.Read())
            {
                var colName = colReader["name"]?.ToString();
                if (string.Equals(colName, "CreditType", StringComparison.OrdinalIgnoreCase)) hasCreditType = true;
                if (string.Equals(colName, "IsPaidOff", StringComparison.OrdinalIgnoreCase)) hasIsPaidOff = true;
                if (string.Equals(colName, "PaidOffDate", StringComparison.OrdinalIgnoreCase)) hasPaidOffDate = true;
            }

            if (!hasCreditType)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE ExpenditureCredits ADD COLUMN CreditType TEXT NOT NULL DEFAULT 'GIVEN_TO_US';";
                alterCmd.ExecuteNonQuery();
            }
            if (!hasIsPaidOff)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE ExpenditureCredits ADD COLUMN IsPaidOff INTEGER NOT NULL DEFAULT 0;";
                alterCmd.ExecuteNonQuery();
            }
            if (!hasPaidOffDate)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE ExpenditureCredits ADD COLUMN PaidOffDate TEXT;";
                alterCmd.ExecuteNonQuery();
            }
        }

        // Ensure IsUnpaid column exists in Expenditures table
        using (var checkCol = conn.CreateCommand())
        {
            checkCol.CommandText = "PRAGMA table_info(Expenditures);";
            bool hasIsUnpaid = false;
            using var colReader = checkCol.ExecuteReader();
            while (colReader.Read())
            {
                if (string.Equals(colReader["name"]?.ToString(), "IsUnpaid", StringComparison.OrdinalIgnoreCase))
                {
                    hasIsUnpaid = true;
                    break;
                }
            }
            if (!hasIsUnpaid)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Expenditures ADD COLUMN IsUnpaid INTEGER NOT NULL DEFAULT 0;";
                alterCmd.ExecuteNonQuery();
            }
        }

        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM Users WHERE UPPER(Email) = 'ADMIN@MYGTS';";
            var adminCount = Convert.ToInt64(countCmd.ExecuteScalar());
            if (adminCount == 0)
            {
                using var seedAdmin = conn.CreateCommand();
                seedAdmin.CommandText = @"
                INSERT INTO Users (Id, Email, DisplayName, PlainPassword, PasswordHash, Role, IsActive, CreatedAt)
                VALUES (
                    'admin-mygts-init',
                    'admin@mygts',
                    'ADMIN',
                    'Admin@123',
                    'v1$t6rUEUv8nXgyuNtgl5Ed9Q==$pa5mc16T4FpLF8ZNajPsGWciYPHQZsEi+rHNJWba/DA=',
                    'ADMIN',
                    1,
                    datetime('now')
                );";
                seedAdmin.ExecuteNonQuery();
            }

            countCmd.CommandText = "SELECT COUNT(*) FROM Users WHERE UPPER(Email) = 'ADMIN@EXPENDITURE';";
            var expAdminCount = Convert.ToInt64(countCmd.ExecuteScalar());
            if (expAdminCount == 0)
            {
                using var seedExp = conn.CreateCommand();
                seedExp.CommandText = @"
                INSERT INTO Users (Id, Email, DisplayName, PlainPassword, PasswordHash, Role, IsActive, CreatedAt)
                VALUES (
                    'admin-expenditure-init',
                    'admin@expenditure',
                    'EXPENDITURE ADMIN',
                    'Admin@123',
                    'v1$t6rUEUv8nXgyuNtgl5Ed9Q==$pa5mc16T4FpLF8ZNajPsGWciYPHQZsEi+rHNJWba/DA=',
                    'ADMIN',
                    1,
                    datetime('now')
                );";
                seedExp.ExecuteNonQuery();
            }

            countCmd.CommandText = "SELECT COUNT(*) FROM Users WHERE UPPER(Email) = 'ADMIN2@MYGTS';";
            var admin2Count = Convert.ToInt64(countCmd.ExecuteScalar());
            if (admin2Count == 0)
            {
                using var seedAdmin2 = conn.CreateCommand();
                seedAdmin2.CommandText = @"
                INSERT INTO Users (Id, Email, DisplayName, PlainPassword, PasswordHash, Role, IsActive, CreatedAt)
                VALUES (
                    'admin2-mygts-init',
                    'ADMIN2@MYGTS',
                    'ADMIN_2',
                    'Myacc@120',
                    'v1$r1sQv2/F8x8y0k2v1g2Q==$d7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a2b3c4d5e6f7a=',
                    'ADMIN',
                    1,
                    datetime('now')
                );";
                seedAdmin2.ExecuteNonQuery();
            }
        }
    }

        private SqliteConnection CreateMainDbConnection()
    {
        var conn = new SqliteConnection($"Data Source={_mainDbPath};Mode=ReadOnly;Cache=Shared;");
        conn.Open();
        return conn;
    }

    private async Task<List<TransactionRecord>> GetAllBranchTransactionsAsync()
    {
        var list = new List<TransactionRecord>();
        if (!File.Exists(_mainDbPath)) return list;

        using var conn = CreateMainDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Branch, Date, ActivationDate, VehicleNo, Vendor, PaymentFor, PaidTo, PaymentMode, Amount, Profit, Utr, Notes, CreatedAt, BatchId FROM Transactions;";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var t = new TransactionRecord
            {
                Id = reader["Id"]?.ToString() ?? "",
                Branch = reader["Branch"]?.ToString() ?? "DMG",
                Date = reader["Date"]?.ToString() ?? "",
                ActivationDate = reader["ActivationDate"]?.ToString() ?? "",
                VehicleNo = reader["VehicleNo"]?.ToString() ?? "",
                Vendor = reader["Vendor"]?.ToString() ?? "",
                PaymentFor = reader["PaymentFor"]?.ToString() ?? "",
                PaidTo = reader["PaidTo"]?.ToString() ?? "",
                PaymentMode = reader["PaymentMode"]?.ToString() ?? "",
                Utr = reader["Utr"]?.ToString() ?? "",
                Notes = reader["Notes"]?.ToString() ?? "",
                CreatedAt = reader["CreatedAt"]?.ToString() ?? "",
                BatchId = reader["BatchId"]?.ToString() ?? ""
            };

            if (decimal.TryParse(reader["Amount"]?.ToString(), out var amt)) t.Amount = amt;
            if (decimal.TryParse(reader["Profit"]?.ToString(), out var prf)) t.Profit = prf;

            list.Add(t);
        }
        return list;
    }

    public async Task<ExpenditureListViewModel> GetExpendituresAsync(ExpenditureFilterViewModel filters)
    {
        int resolvedFy;
        if (filters.FinancialYear.HasValue && filters.FinancialYear.Value > 0)
        {
            resolvedFy = filters.FinancialYear.Value;
        }
        else if (filters.FinancialYear.HasValue && filters.FinancialYear.Value == 0)
        {
            // Explicitly requested ALL financial years (no date cap)
            resolvedFy = 0;
        }
        else if (filters.IsPreviousYears)
        {
            resolvedFy = 0;
        }
        else
        {
            resolvedFy = FinancialYearHelper.CurrentFinancialYearStart;
        }

        if (resolvedFy > 0)
        {
            var (fyStart, fyEnd) = FinancialYearHelper.GetDateRange(resolvedFy);
            if (string.IsNullOrWhiteSpace(filters.StartDate) && string.IsNullOrWhiteSpace(filters.EndDate))
            {
                filters.StartDate = fyStart;
                filters.EndDate = fyEnd;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(filters.StartDate)) filters.StartDate = fyStart;
                if (string.IsNullOrWhiteSpace(filters.EndDate)) filters.EndDate = fyEnd;
            }
        }
        else if (filters.IsPreviousYears && (!filters.FinancialYear.HasValue || filters.FinancialYear.Value < 0))
        {
            var (currStart, _) = FinancialYearHelper.GetDateRange(FinancialYearHelper.CurrentFinancialYearStart);
            var prevCap = DateTime.Parse(currStart).AddDays(-1).ToString("yyyy-MM-dd");
            if (string.IsNullOrWhiteSpace(filters.EndDate) || string.Compare(filters.EndDate, prevCap, StringComparison.OrdinalIgnoreCase) > 0)
            {
                filters.EndDate = prevCap;
            }
        }

        var result = new ExpenditureListViewModel
        {
            Filters = filters,
            CurrentPage = filters.Page > 0 ? filters.Page : 1,
            PageSize = filters.PageSize > 0 ? filters.PageSize : 25,
            SelectedFinancialYear = resolvedFy
        };

        result.AvailableBanks = await GetDropdownOptionsAsync("Bank");
        result.AvailablePaymentTypes = await GetDropdownOptionsAsync("PaymentType");
        result.AvailableCompanies = await GetAllCompaniesAsync();

        // 1. Fetch all transactions across DMG & RTO directly from main DB (unfiltered by current branch)
        var allTxnsList = await GetAllBranchTransactionsAsync();
        var allTxns = allTxnsList.AsEnumerable();

        // Enrich dropdown options from transactions
        var txnBanks = allTxns.Select(t => t.PaidTo?.Trim()).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var b in txnBanks)
        {
            if (!result.AvailableBanks.Any(x => x.Equals(b, StringComparison.OrdinalIgnoreCase)))
                result.AvailableBanks.Add(b!);
        }

        var txnModes = allTxns.Select(t => t.PaymentMode?.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var m in txnModes)
        {
            if (!result.AvailablePaymentTypes.Any(x => x.Equals(m, StringComparison.OrdinalIgnoreCase)))
                result.AvailablePaymentTypes.Add(m!);
        }

        var txnVendors = allTxns.Select(t => t.Vendor?.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var v in txnVendors)
        {
            if (!result.AvailableCompanies.Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase)))
                result.AvailableCompanies.Add(v!);
        }

        // Filter transactions according to user selections
        var txns = allTxns;

        // Branch filter
        if (!string.IsNullOrWhiteSpace(filters.Branch) && !filters.Branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (filters.Branch.Equals("EXPENDITURE", StringComparison.OrdinalIgnoreCase))
            {
                txns = Enumerable.Empty<TransactionRecord>();
            }
            else if (filters.Branch.Equals("DMG", StringComparison.OrdinalIgnoreCase))
            {
                txns = txns.Where(t => string.Equals(t.Branch, "DMG", StringComparison.OrdinalIgnoreCase));
            }
            else if (filters.Branch.Equals("RTO", StringComparison.OrdinalIgnoreCase))
            {
                txns = txns.Where(t => string.Equals(t.Branch, "RTO", StringComparison.OrdinalIgnoreCase));
            }
        }

        // Financial Year and Date scoping
        if (resolvedFy > 0)
        {
            txns = txns.Where(t => FinancialYearHelper.BelongsToFinancialYear(t.EffectiveDate, resolvedFy));
        }
        else if (filters.IsPreviousYears)
        {
            txns = txns.Where(t => FinancialYearHelper.GetFinancialYearStart(t.EffectiveDate) < FinancialYearHelper.CurrentFinancialYearStart);
        }

        // Custom Date filters
        if (!string.IsNullOrWhiteSpace(filters.StartDate))
        {
            if (DateTime.TryParse(filters.StartDate.Trim(), out var sD))
                txns = txns.Where(t => DateTime.TryParse(t.EffectiveDate, out var td) && td >= sD.Date);
            else
                txns = txns.Where(t => string.Compare(t.EffectiveDate, filters.StartDate.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }
        if (!string.IsNullOrWhiteSpace(filters.EndDate))
        {
            if (DateTime.TryParse(filters.EndDate.Trim(), out var eD))
                txns = txns.Where(t => DateTime.TryParse(t.EffectiveDate, out var td) && td <= eD.Date);
            else
                txns = txns.Where(t => string.Compare(t.EffectiveDate, filters.EndDate.Trim(), StringComparison.OrdinalIgnoreCase) <= 0);
        }

        // CashFlow filter: Transactions are customer payments (CREDITED). If user specifically asks for DEBITED only, exclude transactions.
        if (string.Equals(filters.CashFlow, "DEBITED", StringComparison.OrdinalIgnoreCase))
        {
            txns = Enumerable.Empty<TransactionRecord>();
        }

        // DocumentType filter: If user filters for PI or INVOICE specifically, exclude customer receipts.
        if (!string.IsNullOrWhiteSpace(filters.DocumentType) && !filters.DocumentType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            txns = Enumerable.Empty<TransactionRecord>();
        }

        // Bank filter
        if (!string.IsNullOrWhiteSpace(filters.Bank) && !filters.Bank.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            txns = txns.Where(t => (t.PaidTo ?? "").Equals(filters.Bank.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // PaymentType filter
        if (!string.IsNullOrWhiteSpace(filters.PaymentType) && !filters.PaymentType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            txns = txns.Where(t => (t.PaymentMode ?? "").Equals(filters.PaymentType.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Company filter
        if (!string.IsNullOrWhiteSpace(filters.Company) && !filters.Company.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            txns = txns.Where(t => (t.Vendor ?? "").Contains(filters.Company.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Global Query filter
        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var q = filters.Query.Trim().ToLowerInvariant();
            txns = txns.Where(t => 
                (t.VehicleNo ?? "").ToLowerInvariant().Contains(q) ||
                (t.Vendor ?? "").ToLowerInvariant().Contains(q) ||
                (t.PaymentFor ?? "").ToLowerInvariant().Contains(q) ||
                (t.PaidTo ?? "").ToLowerInvariant().Contains(q) ||
                (t.Utr ?? "").ToLowerInvariant().Contains(q) ||
                (t.Notes ?? "").ToLowerInvariant().Contains(q) ||
                (t.Amount?.ToString() ?? "").Contains(q));
        }

        var matchingTxns = txns.ToList();

        // 2. Query direct Expenditures
        var expRecords = new List<ExpenditureRecord>();
        var skipDirectExp = !string.IsNullOrWhiteSpace(filters.Branch) && 
            (filters.Branch.Equals("DMG", StringComparison.OrdinalIgnoreCase) || filters.Branch.Equals("RTO", StringComparison.OrdinalIgnoreCase));

        if (!skipDirectExp)
        {
            using var conn = CreateConnection();
            var whereClauses = new List<string>();
            var parameters = new List<SqliteParameter>();

            BuildFilterQuery(filters, whereClauses, parameters);

            var whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

            // KPI Query for direct expenditures
            var kpiSql = $@"
                SELECT 
                    COALESCE(SUM(CASE WHEN UPPER(CashFlow) = 'CREDITED' THEN Amount ELSE 0 END), 0) AS TotalCredited,
                    COALESCE(SUM(CASE WHEN UPPER(CashFlow) = 'DEBITED' THEN Amount ELSE 0 END), 0) AS TotalDebited,
                    COALESCE(SUM(CASE WHEN (UPPER(DocumentType) = 'PI' AND IsConvertedToInvoice = 0) THEN 1 ELSE 0 END), 0) AS PendingPiCount,
                    COALESCE(SUM(CASE WHEN (UPPER(DocumentType) = 'PI' AND IsConvertedToInvoice = 0) THEN Amount ELSE 0 END), 0) AS PendingPiAmount,
                    COALESCE(SUM(CASE WHEN (UPPER(DocumentType) = 'INVOICE' OR IsConvertedToInvoice = 1) THEN 1 ELSE 0 END), 0) AS InvoiceCount,
                    COALESCE(SUM(CASE WHEN (UPPER(DocumentType) = 'INVOICE' OR IsConvertedToInvoice = 1) THEN Amount ELSE 0 END), 0) AS InvoiceAmount,
                    COUNT(*) AS TotalCount
                FROM Expenditures
                {whereSql};";

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = kpiSql;
                foreach (var p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    result.DirectExpInflow = Convert.ToDecimal(reader["TotalCredited"]);
                    result.TotalDebited = Convert.ToDecimal(reader["TotalDebited"]);
                    result.PendingPiCount = Convert.ToInt32(reader["PendingPiCount"]);
                    result.PendingPiAmount = Convert.ToDecimal(reader["PendingPiAmount"]);
                    result.InvoiceCount = Convert.ToInt32(reader["InvoiceCount"]);
                    result.InvoiceAmount = Convert.ToDecimal(reader["InvoiceAmount"]);
                }
            }

            // Fetch matching direct records
            var querySql = $@"
                SELECT Id, RecordDate, DocumentType, ReferenceNumber, Company, Purpose, Bank, PaymentType, 
                       CashFlow, Utr, Amount, PaymentDate, IsConvertedToInvoice, OriginalPiNumber, 
                       OriginalPiDate, InvoiceNumber, InvoiceDate, Notes, CreatedAt, CreatedBy, IsUnpaid
                FROM Expenditures
                {whereSql};";

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = querySql;
                foreach (var p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    expRecords.Add(ReadRecord(reader));
                }
            }
        }

        // Inflow breakdown
        result.DmgInflow = matchingTxns.Where(t => string.Equals(t.Branch, "DMG", StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount ?? 0m);
        result.RtoInflow = matchingTxns.Where(t => string.Equals(t.Branch, "RTO", StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount ?? 0m);
        result.TotalTransactionProfit = matchingTxns.Sum(t => t.Profit ?? 0m);
        result.TotalCredited = result.DirectExpInflow;

        // Separate Invoices & PIs: result.Records holds exclusively direct Invoices & PIs
        var sortedExp = expRecords
            .OrderByDescending(r => r.DisplayDate)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();

        result.TotalCount = sortedExp.Count;
        result.Records = sortedExp
            .Skip((result.CurrentPage - 1) * result.PageSize)
            .Take(result.PageSize)
            .ToList();

        return result;
    }

    private void BuildFilterQuery(ExpenditureFilterViewModel filters, List<string> whereClauses, List<SqliteParameter> parameters)
    {
        if (!string.IsNullOrWhiteSpace(filters.StartDate))
        {
            whereClauses.Add("(CASE WHEN IsConvertedToInvoice = 1 AND InvoiceDate IS NOT NULL AND InvoiceDate != '' THEN InvoiceDate ELSE RecordDate END) >= @StartDate");
            parameters.Add(new SqliteParameter("@StartDate", filters.StartDate.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filters.EndDate))
        {
            whereClauses.Add("(CASE WHEN IsConvertedToInvoice = 1 AND InvoiceDate IS NOT NULL AND InvoiceDate != '' THEN InvoiceDate ELSE RecordDate END) <= @EndDate");
            parameters.Add(new SqliteParameter("@EndDate", filters.EndDate.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filters.DocumentType) && !string.Equals(filters.DocumentType, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(filters.DocumentType, "INVOICE", StringComparison.OrdinalIgnoreCase))
            {
                whereClauses.Add("(UPPER(DocumentType) = 'INVOICE' OR IsConvertedToInvoice = 1)");
            }
            else if (string.Equals(filters.DocumentType, "PI", StringComparison.OrdinalIgnoreCase))
            {
                whereClauses.Add("(UPPER(DocumentType) = 'PI' AND IsConvertedToInvoice = 0)");
            }
        }

        if (!string.IsNullOrWhiteSpace(filters.Bank) && !string.Equals(filters.Bank, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            whereClauses.Add("UPPER(Bank) = @Bank");
            parameters.Add(new SqliteParameter("@Bank", filters.Bank.Trim().ToUpperInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(filters.PaymentType) && !string.Equals(filters.PaymentType, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            whereClauses.Add("UPPER(PaymentType) = @PaymentType");
            parameters.Add(new SqliteParameter("@PaymentType", filters.PaymentType.Trim().ToUpperInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(filters.Company) && !string.Equals(filters.Company, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            whereClauses.Add("UPPER(Company) = @Company");
            parameters.Add(new SqliteParameter("@Company", filters.Company.Trim().ToUpperInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(filters.CashFlow) && !string.Equals(filters.CashFlow, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            whereClauses.Add("UPPER(CashFlow) = @CashFlow");
            parameters.Add(new SqliteParameter("@CashFlow", filters.CashFlow.Trim().ToUpperInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var q = $"%{filters.Query.Trim().ToUpperInvariant()}%";
            whereClauses.Add(@"
                (UPPER(ReferenceNumber) LIKE @Q OR 
                 UPPER(InvoiceNumber) LIKE @Q OR 
                 UPPER(OriginalPiNumber) LIKE @Q OR 
                 UPPER(Company) LIKE @Q OR 
                 UPPER(Purpose) LIKE @Q OR 
                 UPPER(Bank) LIKE @Q OR 
                 UPPER(Utr) LIKE @Q OR 
                 CAST(Amount AS TEXT) LIKE @Q)");
            parameters.Add(new SqliteParameter("@Q", q));
        }
    }

    private static bool HasColumn(SqliteDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static ExpenditureRecord ReadRecord(SqliteDataReader reader)
    {
        return new ExpenditureRecord
        {
            Id = reader["Id"].ToString()!,
            RecordDate = reader["RecordDate"].ToString()!,
            DocumentType = reader["DocumentType"].ToString()!,
            ReferenceNumber = reader["ReferenceNumber"].ToString()!,
            Company = reader["Company"].ToString()!,
            Purpose = reader["Purpose"].ToString()!,
            Bank = reader["Bank"].ToString()!,
            PaymentType = reader["PaymentType"].ToString()!,
            CashFlow = reader["CashFlow"].ToString()!,
            Utr = reader["Utr"] is DBNull ? null : reader["Utr"].ToString(),
            Amount = Convert.ToDecimal(reader["Amount"]),
            PaymentDate = reader["PaymentDate"] is DBNull ? null : reader["PaymentDate"].ToString(),
            IsConvertedToInvoice = Convert.ToInt32(reader["IsConvertedToInvoice"]) == 1,
            OriginalPiNumber = reader["OriginalPiNumber"] is DBNull ? null : reader["OriginalPiNumber"].ToString(),
            OriginalPiDate = reader["OriginalPiDate"] is DBNull ? null : reader["OriginalPiDate"].ToString(),
            InvoiceNumber = reader["InvoiceNumber"] is DBNull ? null : reader["InvoiceNumber"].ToString(),
            InvoiceDate = reader["InvoiceDate"] is DBNull ? null : reader["InvoiceDate"].ToString(),
            Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString(),
            CreatedAt = reader["CreatedAt"].ToString()!,
            CreatedBy = reader["CreatedBy"] is DBNull ? null : reader["CreatedBy"].ToString(),
            IsUnpaid = HasColumn(reader, "IsUnpaid") && reader["IsUnpaid"] is not DBNull && Convert.ToInt32(reader["IsUnpaid"]) == 1
        };
    }

    public async Task<ExpenditureRecord?> GetExpenditureByIdAsync(string id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Expenditures WHERE Id = @Id LIMIT 1;";
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadRecord(reader);
        }
        return null;
    }

    public async Task CreateExpenditureAsync(ExpenditureRecord record)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        INSERT INTO Expenditures (
            Id, RecordDate, DocumentType, ReferenceNumber, Company, Purpose, Bank, PaymentType, 
            CashFlow, Utr, Amount, PaymentDate, IsConvertedToInvoice, OriginalPiNumber, 
            OriginalPiDate, InvoiceNumber, InvoiceDate, Notes, CreatedAt, CreatedBy, IsUnpaid
        ) VALUES (
            @Id, @RecordDate, @DocumentType, @ReferenceNumber, @Company, @Purpose, @Bank, @PaymentType, 
            @CashFlow, @Utr, @Amount, @PaymentDate, @IsConvertedToInvoice, @OriginalPiNumber, 
            @OriginalPiDate, @InvoiceNumber, @InvoiceDate, @Notes, @CreatedAt, @CreatedBy, @IsUnpaid
        );";

        AddRecordParameters(cmd, record);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateExpenditureAsync(ExpenditureRecord record)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        UPDATE Expenditures SET 
            RecordDate = @RecordDate,
            DocumentType = @DocumentType,
            ReferenceNumber = @ReferenceNumber,
            Company = @Company,
            Purpose = @Purpose,
            Bank = @Bank,
            PaymentType = @PaymentType,
            CashFlow = @CashFlow,
            Utr = @Utr,
            Amount = @Amount,
            PaymentDate = @PaymentDate,
            IsConvertedToInvoice = @IsConvertedToInvoice,
            OriginalPiNumber = @OriginalPiNumber,
            OriginalPiDate = @OriginalPiDate,
            InvoiceNumber = @InvoiceNumber,
            InvoiceDate = @InvoiceDate,
            Notes = @Notes,
            IsUnpaid = @IsUnpaid
        WHERE Id = @Id;";

        AddRecordParameters(cmd, record);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void AddRecordParameters(SqliteCommand cmd, ExpenditureRecord r)
    {
        cmd.Parameters.AddWithValue("@Id", (object?)r.Id ?? Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@RecordDate", (object?)r.RecordDate?.Trim().ToUpperInvariant() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@DocumentType", (object?)r.DocumentType?.Trim().ToUpperInvariant() ?? "INVOICE");
        cmd.Parameters.AddWithValue("@ReferenceNumber", (object?)r.ReferenceNumber?.Trim().ToUpperInvariant() ?? "-");
        cmd.Parameters.AddWithValue("@Company", (object?)r.Company?.Trim().ToUpperInvariant() ?? "-");
        cmd.Parameters.AddWithValue("@Purpose", (object?)r.Purpose?.Trim().ToUpperInvariant() ?? "-");
        cmd.Parameters.AddWithValue("@Bank", (object?)r.Bank?.Trim().ToUpperInvariant() ?? "-");
        cmd.Parameters.AddWithValue("@PaymentType", (object?)r.PaymentType?.Trim().ToUpperInvariant() ?? "-");
        cmd.Parameters.AddWithValue("@CashFlow", (object?)r.CashFlow?.Trim().ToUpperInvariant() ?? "DEBITED");
        cmd.Parameters.AddWithValue("@Utr", (object?)r.Utr?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Amount", r.Amount);
        cmd.Parameters.AddWithValue("@PaymentDate", (object?)r.PaymentDate?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsConvertedToInvoice", r.IsConvertedToInvoice ? 1 : 0);
        cmd.Parameters.AddWithValue("@OriginalPiNumber", (object?)r.OriginalPiNumber?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OriginalPiDate", (object?)r.OriginalPiDate?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InvoiceNumber", (object?)r.InvoiceNumber?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@InvoiceDate", (object?)r.InvoiceDate?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)r.Notes?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", (object?)r.CreatedAt ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@CreatedBy", (object?)r.CreatedBy?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsUnpaid", r.IsUnpaid ? 1 : 0);
    }

    public async Task DeleteExpenditureAsync(string id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Expenditures WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    private static ExpenditureCreditRecord ReadCreditRecord(SqliteDataReader reader)
    {
        return new ExpenditureCreditRecord
        {
            Id = reader["Id"].ToString()!,
            CompanyName = reader["CompanyName"]?.ToString() ?? "",
            CreditAmount = Convert.ToDecimal(reader["CreditAmount"]),
            CreditType = HasColumn(reader, "CreditType") && reader["CreditType"] is not DBNull ? reader["CreditType"].ToString()! : "GIVEN_TO_US",
            IsPaidOff = HasColumn(reader, "IsPaidOff") && reader["IsPaidOff"] is not DBNull && Convert.ToInt32(reader["IsPaidOff"]) == 1,
            PaidOffDate = HasColumn(reader, "PaidOffDate") && reader["PaidOffDate"] is not DBNull ? reader["PaidOffDate"].ToString() : null,
            Date = reader["Date"] is DBNull ? "" : reader["Date"].ToString()!,
            Description = reader["Description"] is DBNull ? null : reader["Description"].ToString(),
            Notes = reader["Notes"] is DBNull ? null : reader["Notes"].ToString(),
            CreatedAt = reader["CreatedAt"].ToString()!,
            CreatedBy = reader["CreatedBy"] is DBNull ? null : reader["CreatedBy"].ToString()
        };
    }

    public async Task<List<ExpenditureCreditRecord>> GetAllCreditsAsync()
    {
        var list = new List<ExpenditureCreditRecord>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ExpenditureCredits ORDER BY Date DESC, CreatedAt DESC;";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadCreditRecord(reader));
        }
        return list;
    }

    public async Task<ExpenditureCreditRecord?> GetCreditByIdAsync(string id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ExpenditureCredits WHERE Id = @Id LIMIT 1;";
        cmd.Parameters.AddWithValue("@Id", id);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadCreditRecord(reader);
        }
        return null;
    }

    public async Task CreateCreditAsync(ExpenditureCreditRecord credit)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        INSERT INTO ExpenditureCredits (
            Id, CompanyName, CreditAmount, CreditType, IsPaidOff, PaidOffDate, Date, Description, Notes, CreatedAt, CreatedBy
        ) VALUES (
            @Id, @CompanyName, @CreditAmount, @CreditType, @IsPaidOff, @PaidOffDate, @Date, @Description, @Notes, @CreatedAt, @CreatedBy
        );";
        cmd.Parameters.AddWithValue("@Id", credit.Id ?? Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@CompanyName", credit.CompanyName?.Trim().ToUpperInvariant() ?? "-");
        cmd.Parameters.AddWithValue("@CreditAmount", credit.CreditAmount);
        cmd.Parameters.AddWithValue("@CreditType", string.Equals(credit.CreditType, "GIVEN_BY_US", StringComparison.OrdinalIgnoreCase) ? "GIVEN_BY_US" : "GIVEN_TO_US");
        cmd.Parameters.AddWithValue("@IsPaidOff", credit.IsPaidOff ? 1 : 0);
        cmd.Parameters.AddWithValue("@PaidOffDate", (object?)credit.PaidOffDate?.Trim() ?? (credit.IsPaidOff ? DateTime.UtcNow.ToString("yyyy-MM-dd") : DBNull.Value));
        cmd.Parameters.AddWithValue("@Date", (object?)credit.Date?.Trim() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@Description", (object?)credit.Description?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)credit.Notes?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", (object?)credit.CreatedAt ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@CreatedBy", (object?)credit.CreatedBy?.Trim().ToUpperInvariant() ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateCreditAsync(ExpenditureCreditRecord credit)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        UPDATE ExpenditureCredits SET 
            CompanyName = @CompanyName,
            CreditAmount = @CreditAmount,
            CreditType = @CreditType,
            IsPaidOff = @IsPaidOff,
            PaidOffDate = @PaidOffDate,
            Date = @Date,
            Description = @Description,
            Notes = @Notes
        WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", credit.Id);
        cmd.Parameters.AddWithValue("@CompanyName", credit.CompanyName?.Trim().ToUpperInvariant() ?? "-");
        cmd.Parameters.AddWithValue("@CreditAmount", credit.CreditAmount);
        cmd.Parameters.AddWithValue("@CreditType", string.Equals(credit.CreditType, "GIVEN_BY_US", StringComparison.OrdinalIgnoreCase) ? "GIVEN_BY_US" : "GIVEN_TO_US");
        cmd.Parameters.AddWithValue("@IsPaidOff", credit.IsPaidOff ? 1 : 0);
        cmd.Parameters.AddWithValue("@PaidOffDate", (object?)credit.PaidOffDate?.Trim() ?? (credit.IsPaidOff ? DateTime.UtcNow.ToString("yyyy-MM-dd") : DBNull.Value));
        cmd.Parameters.AddWithValue("@Date", (object?)credit.Date?.Trim() ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@Description", (object?)credit.Description?.Trim().ToUpperInvariant() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Notes", (object?)credit.Notes?.Trim().ToUpperInvariant() ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ToggleCreditPaidOffAsync(string id)
    {
        var existing = await GetCreditByIdAsync(id);
        if (existing == null) return;

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        bool newStatus = !existing.IsPaidOff;
        string? newPaidDate = newStatus ? DateTime.UtcNow.ToString("yyyy-MM-dd") : null;

        cmd.CommandText = @"
        UPDATE ExpenditureCredits SET 
            IsPaidOff = @IsPaidOff,
            PaidOffDate = @PaidOffDate
        WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@IsPaidOff", newStatus ? 1 : 0);
        cmd.Parameters.AddWithValue("@PaidOffDate", (object?)newPaidDate ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteCreditAsync(string id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ExpenditureCredits WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> GetDropdownOptionsAsync(string category)
    {
        var options = new List<string>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM DropdownOptions WHERE UPPER(Category) = @Category AND IsActive = 1 ORDER BY Value ASC;";
        cmd.Parameters.AddWithValue("@Category", category.Trim().ToUpperInvariant());

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            options.Add(reader["Value"].ToString()!.ToUpperInvariant());
        }
        return options;
    }

    public async Task<bool> AddDropdownOptionAsync(string category, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var cleanVal = value.Trim().ToUpperInvariant();
        var cleanCat = category.Trim();

        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM DropdownOptions WHERE UPPER(Category) = @Cat AND UPPER(Value) = @Val;";
        cmd.Parameters.AddWithValue("@Cat", cleanCat.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@Val", cleanVal);

        var exists = Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        if (exists) return true;

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO DropdownOptions (Category, Value, IsActive) VALUES (@Cat, @Val, 1);";
        insertCmd.Parameters.AddWithValue("@Cat", cleanCat);
        insertCmd.Parameters.AddWithValue("@Val", cleanVal);
        await insertCmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<List<string>> GetAllCompaniesAsync()
    {
        var list = new List<string>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT UPPER(Company) AS Company FROM Expenditures WHERE Company IS NOT NULL AND Company != '' ORDER BY Company ASC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader["Company"].ToString()!);
        }
        return list;
    }

    public async Task<ExpenditureUserRecord?> GetUserByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var clean = email.Trim().ToUpperInvariant();
        using var conn = CreateConnection();

        // 1. Exact match on Email or DisplayName
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users WHERE UPPER(Email) = @Email OR UPPER(DisplayName) = @Email LIMIT 1;";
        cmd.Parameters.AddWithValue("@Email", clean);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadUser(reader);
        }

        // 2. Fallbacks: if user typed email with .COM, try without .COM
        if (clean.EndsWith(".COM"))
        {
            var withoutCom = clean[..^4];
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT * FROM Users WHERE UPPER(Email) = @Email LIMIT 1;";
            cmd2.Parameters.AddWithValue("@Email", withoutCom);
            using var reader2 = await cmd2.ExecuteReaderAsync();
            if (await reader2.ReadAsync()) return ReadUser(reader2);
        }

        // 3. Fallbacks: if user typed just username (e.g. "admin"), try "@mygts" and "@expenditure"
        if (!clean.Contains('@'))
        {
            var withMygts = clean + "@MYGTS";
            var withExp = clean + "@EXPENDITURE";
            using var cmd3 = conn.CreateCommand();
            cmd3.CommandText = "SELECT * FROM Users WHERE UPPER(Email) IN (@E1, @E2) LIMIT 1;";
            cmd3.Parameters.AddWithValue("@E1", withMygts);
            cmd3.Parameters.AddWithValue("@E2", withExp);
            using var reader3 = await cmd3.ExecuteReaderAsync();
            if (await reader3.ReadAsync()) return ReadUser(reader3);
        }

        return null;
    }

    private static ExpenditureUserRecord ReadUser(SqliteDataReader reader)
    {
        return new ExpenditureUserRecord
        {
            Id = reader["Id"].ToString()!,
            Email = reader["Email"].ToString()!,
            DisplayName = reader["DisplayName"].ToString()!,
            PlainPassword = reader["PlainPassword"].ToString()!,
            PasswordHash = reader["PasswordHash"].ToString()!,
            Role = reader["Role"].ToString()!,
            IsActive = Convert.ToInt32(reader["IsActive"]) == 1,
            ActiveSessionToken = reader["ActiveSessionToken"] is DBNull ? null : reader["ActiveSessionToken"].ToString(),
            CreatedAt = reader["CreatedAt"].ToString()!
        };
    }

    public async Task<List<ExpenditureUserRecord>> GetAllUsersAsync()
    {
        var list = new List<ExpenditureUserRecord>();
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users ORDER BY DisplayName ASC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadUser(reader));
        }
        return list;
    }

    public async Task UpdateUserSessionTokenAsync(string id, string token)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET ActiveSessionToken = @Token WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Token", token);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task CreateUserAsync(ExpenditureUserRecord user)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        INSERT INTO Users (Id, Email, DisplayName, PlainPassword, PasswordHash, Role, IsActive, CreatedAt)
        VALUES (@Id, @Email, @DisplayName, @PlainPassword, @PasswordHash, @Role, @IsActive, @CreatedAt);";

        cmd.Parameters.AddWithValue("@Id", user.Id);
        cmd.Parameters.AddWithValue("@Email", user.Email.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@DisplayName", user.DisplayName.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@PlainPassword", user.PlainPassword.Trim());
        cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("@Role", user.Role.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@IsActive", user.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("@CreatedAt", user.CreatedAt);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateUserAsync(ExpenditureUserRecord user)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
        UPDATE Users SET 
            DisplayName = @DisplayName,
            PlainPassword = @PlainPassword,
            PasswordHash = @PasswordHash,
            Role = @Role,
            IsActive = @IsActive
        WHERE Id = @Id;";

        cmd.Parameters.AddWithValue("@Id", user.Id);
        cmd.Parameters.AddWithValue("@DisplayName", user.DisplayName.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@PlainPassword", user.PlainPassword.Trim());
        cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("@Role", user.Role.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@IsActive", user.IsActive ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteUserAsync(string id)
    {
        using var conn = CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Users WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<byte[]> ExportToCsvAsync(ExpenditureFilterViewModel filters)
    {
        // Use GetExpendituresAsync logic with unlimited page size
        var exportFilters = new ExpenditureFilterViewModel
        {
            Query = filters.Query,
            StartDate = filters.StartDate,
            EndDate = filters.EndDate,
            DocumentType = filters.DocumentType,
            Bank = filters.Bank,
            PaymentType = filters.PaymentType,
            Company = filters.Company,
            CashFlow = filters.CashFlow,
            Branch = filters.Branch,
            Page = 1,
            PageSize = int.MaxValue
        };

        var list = await GetExpendituresAsync(exportFilters);

        var sb = new StringBuilder();
        sb.AppendLine("DATE,BRANCH,TYPE,REFERENCE NUMBER,COMPANY,FOR,BANK,PAYMENT TYPE,CASH FLOW,UTR,AMOUNT,PAYMENT DATE,PI NUMBER,PI DATE,INVOICE NUMBER,INVOICE DATE,NOTES");

        foreach (var rec in list.Records)
        {
            var line = string.Join(",", new[]
            {
                EscapeCsv(rec.DisplayDate),
                EscapeCsv(rec.SourceBranch ?? "Expenditure"),
                EscapeCsv(rec.DisplayDocumentType),
                EscapeCsv(rec.DisplayReferenceNumber),
                EscapeCsv(rec.Company),
                EscapeCsv(rec.Purpose),
                EscapeCsv(rec.Bank),
                EscapeCsv(rec.PaymentType),
                EscapeCsv(rec.CashFlow),
                EscapeCsv(rec.Utr ?? "-"),
                rec.Amount.ToString("F2"),
                EscapeCsv(rec.PaymentDate ?? "-"),
                EscapeCsv(rec.OriginalPiNumber ?? "-"),
                EscapeCsv(rec.OriginalPiDate ?? "-"),
                EscapeCsv(rec.InvoiceNumber ?? "-"),
                EscapeCsv(rec.InvoiceDate ?? "-"),
                EscapeCsv(rec.Notes ?? "-")
            });
            sb.AppendLine(line);
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public async Task<ExpenditureBranchTransactionsViewModel> GetBranchTransactionsAsync(ExpenditureBranchTransactionsFilterViewModel filters)
    {
        int resolvedFy;
        if (filters.FinancialYear.HasValue && filters.FinancialYear.Value > 0)
        {
            resolvedFy = filters.FinancialYear.Value;
        }
        else if (filters.FinancialYear.HasValue && filters.FinancialYear.Value == 0)
        {
            // Explicitly requested ALL financial years (no date cap)
            resolvedFy = 0;
        }
        else if (filters.IsPreviousYears)
        {
            resolvedFy = 0;
        }
        else
        {
            resolvedFy = FinancialYearHelper.CurrentFinancialYearStart;
        }

        if (resolvedFy > 0)
        {
            var (fyStart, fyEnd) = FinancialYearHelper.GetDateRange(resolvedFy);
            if (string.IsNullOrWhiteSpace(filters.StartDate) && string.IsNullOrWhiteSpace(filters.EndDate))
            {
                filters.StartDate = fyStart;
                filters.EndDate = fyEnd;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(filters.StartDate)) filters.StartDate = fyStart;
                if (string.IsNullOrWhiteSpace(filters.EndDate)) filters.EndDate = fyEnd;
            }
        }
        else if (filters.IsPreviousYears && (!filters.FinancialYear.HasValue || filters.FinancialYear.Value < 0))
        {
            var (currStart, _) = FinancialYearHelper.GetDateRange(FinancialYearHelper.CurrentFinancialYearStart);
            var prevCap = DateTime.Parse(currStart).AddDays(-1).ToString("yyyy-MM-dd");
            if (string.IsNullOrWhiteSpace(filters.EndDate) || string.Compare(filters.EndDate, prevCap, StringComparison.OrdinalIgnoreCase) > 0)
            {
                filters.EndDate = prevCap;
            }
        }

        var result = new ExpenditureBranchTransactionsViewModel
        {
            Filters = filters,
            CurrentPage = filters.Page > 0 ? filters.Page : 1,
            PageSize = filters.PageSize > 0 ? filters.PageSize : 25,
            SelectedFinancialYear = resolvedFy
        };

        var allTxnsList = await GetAllBranchTransactionsAsync();

        // Scope transactions by date / financial year bounds for this view
        var fyScoped = allTxnsList.AsEnumerable();
        if (resolvedFy > 0)
        {
            fyScoped = fyScoped.Where(t => FinancialYearHelper.BelongsToFinancialYear(t.EffectiveDate, resolvedFy));
        }
        else if (filters.IsPreviousYears)
        {
            fyScoped = fyScoped.Where(t => FinancialYearHelper.GetFinancialYearStart(t.EffectiveDate) < FinancialYearHelper.CurrentFinancialYearStart);
        }

        if (!string.IsNullOrWhiteSpace(filters.StartDate))
        {
            if (DateTime.TryParse(filters.StartDate.Trim(), out var sD))
                fyScoped = fyScoped.Where(t => DateTime.TryParse(t.EffectiveDate, out var td) && td >= sD.Date);
            else
                fyScoped = fyScoped.Where(t => string.Compare(t.EffectiveDate, filters.StartDate.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }
        if (!string.IsNullOrWhiteSpace(filters.EndDate))
        {
            if (DateTime.TryParse(filters.EndDate.Trim(), out var eD))
                fyScoped = fyScoped.Where(t => DateTime.TryParse(t.EffectiveDate, out var td) && td <= eD.Date);
            else
                fyScoped = fyScoped.Where(t => string.Compare(t.EffectiveDate, filters.EndDate.Trim(), StringComparison.OrdinalIgnoreCase) <= 0);
        }

        var fyScopedList = fyScoped.ToList();

        // Extract available filter options from scoped list
        result.AvailableBanks = fyScopedList.Select(t => t.PaidTo?.Trim()).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(b => b).ToList()!;
        result.AvailablePaymentModes = fyScopedList.Select(t => t.PaymentMode?.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m).ToList()!;
        result.AvailableVendors = fyScopedList.Select(t => t.Vendor?.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v).ToList()!;

        // Compute overall branch totals for this scoped FY
        result.DmgAmount = fyScopedList.Where(t => string.Equals(t.Branch, "DMG", StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount ?? 0m);
        result.DmgCount = fyScopedList.Count(t => string.Equals(t.Branch, "DMG", StringComparison.OrdinalIgnoreCase));
        result.RtoAmount = fyScopedList.Where(t => string.Equals(t.Branch, "RTO", StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount ?? 0m);
        result.RtoCount = fyScopedList.Count(t => string.Equals(t.Branch, "RTO", StringComparison.OrdinalIgnoreCase));

        var query = fyScopedList.AsEnumerable();

        // Branch filter
        if (!string.IsNullOrWhiteSpace(filters.Branch) && !filters.Branch.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => string.Equals(t.Branch, filters.Branch.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Date range
        // Date range overrides on query
        if (!string.IsNullOrWhiteSpace(filters.StartDate))
        {
            if (DateTime.TryParse(filters.StartDate.Trim(), out var sD))
                query = query.Where(t => DateTime.TryParse(t.EffectiveDate, out var td) && td >= sD.Date);
            else
                query = query.Where(t => string.Compare(t.EffectiveDate, filters.StartDate.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }
        if (!string.IsNullOrWhiteSpace(filters.EndDate))
        {
            if (DateTime.TryParse(filters.EndDate.Trim(), out var eD))
                query = query.Where(t => DateTime.TryParse(t.EffectiveDate, out var td) && td <= eD.Date);
            else
                query = query.Where(t => string.Compare(t.EffectiveDate, filters.EndDate.Trim(), StringComparison.OrdinalIgnoreCase) <= 0);
        }

        // PaidTo (Bank)
        if (!string.IsNullOrWhiteSpace(filters.PaidTo) && !filters.PaidTo.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => (t.PaidTo ?? "").Equals(filters.PaidTo.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // PaymentMode
        if (!string.IsNullOrWhiteSpace(filters.PaymentMode) && !filters.PaymentMode.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => (t.PaymentMode ?? "").Equals(filters.PaymentMode.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Vendor / Customer
        if (!string.IsNullOrWhiteSpace(filters.Vendor) && !filters.Vendor.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => (t.Vendor ?? "").Contains(filters.Vendor.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Search query
        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var q = filters.Query.Trim().ToLowerInvariant();
            query = query.Where(t =>
                (t.VehicleNo ?? "").ToLowerInvariant().Contains(q) ||
                (t.Vendor ?? "").ToLowerInvariant().Contains(q) ||
                (t.PaymentFor ?? "").ToLowerInvariant().Contains(q) ||
                (t.PaidTo ?? "").ToLowerInvariant().Contains(q) ||
                (t.PaymentMode ?? "").ToLowerInvariant().Contains(q) ||
                (t.Utr ?? "").ToLowerInvariant().Contains(q) ||
                (t.Notes ?? "").ToLowerInvariant().Contains(q) ||
                (t.BatchId ?? "").ToLowerInvariant().Contains(q) ||
                (t.Amount?.ToString() ?? "").Contains(q));
        }

        var filtered = query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        result.TotalCount = filtered.Count;
        result.TotalAmount = filtered.Sum(t => t.Amount ?? 0m);
        result.TotalProfit = filtered.Sum(t => t.Profit ?? 0m);

        result.Transactions = filtered
            .Skip((result.CurrentPage - 1) * result.PageSize)
            .Take(result.PageSize)
            .ToList();

        return result;
    }

    public async Task<byte[]> ExportBranchTransactionsCsvAsync(ExpenditureBranchTransactionsFilterViewModel filters)
    {
        var exportFilters = new ExpenditureBranchTransactionsFilterViewModel
        {
            Query = filters.Query,
            StartDate = filters.StartDate,
            EndDate = filters.EndDate,
            Branch = filters.Branch,
            PaidTo = filters.PaidTo,
            PaymentMode = filters.PaymentMode,
            Vendor = filters.Vendor,
            Page = 1,
            PageSize = int.MaxValue
        };

        var vm = await GetBranchTransactionsAsync(exportFilters);
        var sb = new StringBuilder();
        sb.AppendLine("DATE,BRANCH,VEHICLE NO,CUSTOMER / VENDOR,PAYMENT FOR,PAID TO (BANK),PAYMENT MODE,UTR,AMOUNT,PROFIT,NOTES,CREATED AT");

        foreach (var t in vm.Transactions)
        {
            var line = string.Join(",", new[]
            {
                EscapeCsv(t.Date),
                EscapeCsv(t.Branch),
                EscapeCsv(t.VehicleNo),
                EscapeCsv(t.Vendor),
                EscapeCsv(t.PaymentFor),
                EscapeCsv(t.PaidTo),
                EscapeCsv(t.PaymentMode),
                EscapeCsv(t.Utr),
                (t.Amount ?? 0m).ToString("F2"),
                (t.Profit ?? 0m).ToString("F2"),
                EscapeCsv(t.Notes),
                EscapeCsv(t.CreatedAt)
            });
            sb.AppendLine(line);
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public async Task<ExpenditurePreviousYearsViewModel> GetPreviousYearsDataAsync(
        int selectedYear, 
        string activeTab, 
        ExpenditureFilterViewModel expFilters, 
        ExpenditureBranchTransactionsFilterViewModel txnFilters)
    {
        var vm = new ExpenditurePreviousYearsViewModel
        {
            SelectedYear = selectedYear,
            ActiveTab = string.IsNullOrWhiteSpace(activeTab) ? "EXPENSES" : activeTab.ToUpperInvariant()
        };

        var currentFyStart = FinancialYearHelper.CurrentFinancialYearStart;

        // 1. Gather all transactions & expenditures
        var allTxnsList = await GetAllBranchTransactionsAsync();

        var allExpList = new List<ExpenditureRecord>();
        using (var conn = CreateConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Expenditures;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                allExpList.Add(ReadRecord(reader));
            }
        }

        // 2. Discover all financial years dynamically from all records present
        var discoveredYears = new HashSet<int>();
        discoveredYears.Add(currentFyStart);     // Ensure current FY is always present

        foreach (var t in allTxnsList)
        {
            var fy = FinancialYearHelper.GetFinancialYearStart(t.EffectiveDate);
            if (fy >= 2000 && fy <= 2100)
            {
                discoveredYears.Add(fy);
            }
        }

        foreach (var e in allExpList)
        {
            var fy = FinancialYearHelper.GetFinancialYearStart(e.DisplayDate);
            if (fy >= 2000 && fy <= 2100)
            {
                discoveredYears.Add(fy);
            }
        }

        var prevYearsSorted = discoveredYears.OrderByDescending(y => y).ToList();
        vm.AvailablePreviousYears = prevYearsSorted;

        // 3. Build summary for each financial year
        decimal overallInflow = 0;
        decimal overallOutflow = 0;

        foreach (var y in prevYearsSorted)
        {
            var yearTxns = allTxnsList.Where(t => FinancialYearHelper.BelongsToFinancialYear(t.EffectiveDate, y)).ToList();
            var yearExp = allExpList.Where(e => FinancialYearHelper.BelongsToFinancialYear(e.DisplayDate, y)).ToList();

            var directInflow = yearExp.Where(e => string.Equals(e.CashFlow, "CREDITED", StringComparison.OrdinalIgnoreCase)).Sum(e => e.Amount);
            var branchInflow = yearTxns.Sum(t => t.Amount ?? 0m);
            var outflow = yearExp.Where(e => string.Equals(e.CashFlow, "DEBITED", StringComparison.OrdinalIgnoreCase)).Sum(e => e.Amount);

            var summary = new FinancialYearSummaryItem
            {
                StartYear = y,
                DirectInflow = directInflow,
                BranchInflow = branchInflow,
                TotalOutflow = outflow,
                InvoiceCount = yearExp.Count(e => e.DisplayDocumentType == "INVOICE"),
                PiCount = yearExp.Count(e => e.DisplayDocumentType == "PI"),
                TransactionCount = yearTxns.Count
            };

            vm.YearlySummaries.Add(summary);
            overallInflow += summary.TotalInflow;
            overallOutflow += summary.TotalOutflow;
        }

        vm.OverallPreviousInflow = overallInflow;
        vm.OverallPreviousOutflow = overallOutflow;

        // 4. Fetch scoped detailed data for the selected financial year (0 = all years)
        expFilters.FinancialYear = selectedYear;
        expFilters.IsPreviousYears = (selectedYear > 0 && selectedYear < currentFyStart);
        vm.ExpensesModel = await GetExpendituresAsync(expFilters);

        txnFilters.FinancialYear = selectedYear;
        txnFilters.IsPreviousYears = (selectedYear > 0 && selectedYear < currentFyStart);
        vm.TransactionsModel = await GetBranchTransactionsAsync(txnFilters);

        return vm;
    }

    private static string EscapeCsv(string val)
    {
        if (string.IsNullOrEmpty(val)) return "";
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }
}
