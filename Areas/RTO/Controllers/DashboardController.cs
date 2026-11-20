using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using MygtsMvc.Models;

using MygtsMvc.Services;

using MygtsMvc.ViewModels;



namespace MygtsMvc.Areas.RTO.Controllers;

using Microsoft.AspNetCore.Mvc;



[Area("RTO")]

[Authorize]

[RequireModulePermission("Dashboard", PermissionLevel.View)]

public sealed class DashboardController : Controller

{

    private readonly IAppDataStore _store;

    private readonly DashboardService _dashboards;



    public DashboardController(IAppDataStore store, DashboardService dashboards)

    {

        _store = store;

        _dashboards = dashboards;

    }



    [HttpGet]

    public async Task<IActionResult> Index(string? period = "Executive")

    {

        try

        {

            var data = await _store.SnapshotAsync();

            return View(_dashboards.Build(data, period, "RTO"));

        }

        catch (Exception ex)

        {

            TempData["Error"] = $"Dashboard load error: {ex.Message}";

            return RedirectToAction("Error", "Home");

        }

    }



    [HttpGet]

    public async Task<IActionResult> All()

    {

        var data = await _store.SnapshotAsync();

        ViewBag.Periods = new[] { "Executive", "Daily", "Weekly", "Monthly", "Fiscal Year", "Lifetime" };

        return View("All", _dashboards.Build(data, "Executive", "RTO"));

    }



    [HttpGet]
    [AllowAnonymous]
    public IActionResult SwitchBranch(string branch)
    {
        _store.InvalidateCache();

        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        var clean = (branch ?? "").Trim();
        if (string.Equals(clean, "Expenditure", StringComparison.OrdinalIgnoreCase))
        {
            Response.Cookies.Append("mygts_branch", "Expenditure", new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Lax,
                HttpOnly = false,
                IsEssential = true
            });
            return Redirect("/Expenditure/Expenses");
        }

        var target = string.Equals(clean, "RTO", StringComparison.OrdinalIgnoreCase) ? "RTO" : "DMG";
        Response.Cookies.Append("mygts_branch", target, new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            HttpOnly = false,
            IsEssential = true
        });
        return Redirect($"/{target}/Dashboard");
    }
}


