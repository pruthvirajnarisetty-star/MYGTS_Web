using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Controllers;

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
    public IActionResult Index(string? period = "Executive")
    {
        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var query = string.IsNullOrWhiteSpace(period) ? "" : $"?period={Uri.EscapeDataString(period)}";
        return Redirect($"/{branch}/Dashboard{query}");
    }

    [HttpGet]
    public IActionResult All()
    {
        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        return Redirect($"/{branch}/Dashboard/All");
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

