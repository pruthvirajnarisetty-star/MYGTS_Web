using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;

namespace MygtsMvc.Areas.Expenditure.Controllers;

[Area("Expenditure")]
[Authorize]
public class TransactionsController : Controller
{
    private readonly IExpenditureDataStore _store;

    public TransactionsController(IExpenditureDataStore store)
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
    public async Task<IActionResult> Index(ExpenditureBranchTransactionsFilterViewModel filters)
    {
        if (!CheckAccess()) return Forbid();

        Response.Cookies.Append("mygts_branch", "Expenditure", new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            HttpOnly = false
        });

        var vm = await _store.GetBranchTransactionsAsync(filters);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Export(ExpenditureBranchTransactionsFilterViewModel filters)
    {
        if (!CheckAccess()) return Forbid();

        var csvBytes = await _store.ExportBranchTransactionsCsvAsync(filters);
        var filename = $"BRANCH_TRANSACTIONS_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(csvBytes, "text/csv", filename);
    }
}

