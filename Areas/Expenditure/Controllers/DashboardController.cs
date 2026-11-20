using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MygtsMvc.Areas.Expenditure.Controllers;

[Area("Expenditure")]
[Authorize]
public class DashboardController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Expenses", new { area = "Expenditure" });
    }
}

