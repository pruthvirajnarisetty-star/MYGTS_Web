using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Controllers;

public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Error()
    {
        var exceptionFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var msg = exceptionFeature?.Error?.Message ?? TempData["Error"]?.ToString();
        return View(new ErrorViewModel 
        { 
            RequestId = HttpContext.TraceIdentifier,
            ErrorMessage = msg
        });
    }

    [HttpGet]
    public new IActionResult NotFound() => View("NotFound");
}
