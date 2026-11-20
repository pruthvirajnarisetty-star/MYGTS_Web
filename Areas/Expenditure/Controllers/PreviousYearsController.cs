using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;

namespace MygtsMvc.Areas.Expenditure.Controllers;

[Area("Expenditure")]
[Authorize]
public class PreviousYearsController : Controller
{
    private readonly IExpenditureDataStore _store;

    public PreviousYearsController(IExpenditureDataStore store)
    {
        _store = store;
    }

    private bool CheckAccess()
    {
        if (User.IsAdmin()) return true;
        if (User.HasBranchAccess("Expenditure")) return true;
        return false;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        int year = 0, 
        string tab = "EXPENSES", 
        ExpenditureFilterViewModel? expFilters = null, 
        ExpenditureBranchTransactionsFilterViewModel? txnFilters = null)
    {
        if (!CheckAccess()) return Forbid();

        Response.Cookies.Append("mygts_branch", "Expenditure", new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            HttpOnly = false
        });

        expFilters ??= new ExpenditureFilterViewModel();
        txnFilters ??= new ExpenditureBranchTransactionsFilterViewModel();

        var vm = await _store.GetPreviousYearsDataAsync(year, tab, expFilters, txnFilters);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExpenses(int year, ExpenditureFilterViewModel filters)
    {
        if (!CheckAccess()) return Forbid();

        filters.IsPreviousYears = true;
        filters.FinancialYear = year > 0 ? year : null;
        var csvBytes = await _store.ExportToCsvAsync(filters);

        var label = year > 0 ? FinancialYearHelper.FormatLabel(year).Replace(" ", "_") : "ALL_PREVIOUS_YEARS";
        var filename = $"EXPENSES_ARCHIVE_{label}_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(csvBytes, "text/csv", filename);
    }

    [HttpGet]
    public async Task<IActionResult> ExportTransactions(int year, ExpenditureBranchTransactionsFilterViewModel filters)
    {
        if (!CheckAccess()) return Forbid();

        filters.IsPreviousYears = true;
        filters.FinancialYear = year > 0 ? year : null;
        var csvBytes = await _store.ExportBranchTransactionsCsvAsync(filters);

        var label = year > 0 ? FinancialYearHelper.FormatLabel(year).Replace(" ", "_") : "ALL_PREVIOUS_YEARS";
        var filename = $"TRANSACTIONS_ARCHIVE_{label}_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(csvBytes, "text/csv", filename);
    }
}

