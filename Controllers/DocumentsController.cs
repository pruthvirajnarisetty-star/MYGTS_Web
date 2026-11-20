using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace MygtsMvc.Controllers;

[Authorize]
public class DocumentsController : Controller
{
    private readonly IWebHostEnvironment _env;

    public DocumentsController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpGet]
    public IActionResult Index([FromQuery] string? branch)
    {
        if (!string.IsNullOrWhiteSpace(branch))
        {
            Response.Cookies.Append("mygts_branch", branch, new Microsoft.AspNetCore.Http.CookieOptions
            {
                Path = "/",
                MaxAge = System.TimeSpan.FromDays(365),
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                HttpOnly = false
            });
        }
        if (User.IsInRole("Admin"))
        {
            return RedirectToAction(nameof(Src), new { branch });
        }
        return RedirectToAction(nameof(UserManual), new { branch });
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public IActionResult Src([FromQuery] string? branch)
    {
        if (!string.IsNullOrWhiteSpace(branch))
        {
            Response.Cookies.Append("mygts_branch", branch, new Microsoft.AspNetCore.Http.CookieOptions
            {
                Path = "/",
                MaxAge = System.TimeSpan.FromDays(365),
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                HttpOnly = false
            });
        }
        var pdfRelativePath = "/docs/MYGTS_SRC_Specification.pdf";
        var physicalPath = Path.Combine(_env.WebRootPath, "docs", "MYGTS_SRC_Specification.pdf");
        ViewBag.PdfExists = System.IO.File.Exists(physicalPath);
        ViewBag.PdfUrl = pdfRelativePath;
        if (ViewBag.PdfExists)
        {
            var fileInfo = new FileInfo(physicalPath);
            ViewBag.PdfSizeKb = (fileInfo.Length / 1024.0).ToString("0.1");
            ViewBag.PdfLastModified = fileInfo.LastWriteTime.ToString("dd MMM yyyy, HH:mm");
        }
        return View();
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public IActionResult DownloadSrcPdf()
    {
        var physicalPath = Path.Combine(_env.WebRootPath, "docs", "MYGTS_SRC_Specification.pdf");
        if (!System.IO.File.Exists(physicalPath))
        {
            TempData["Error"] = "The SRC specification PDF has not been generated or is missing.";
            return RedirectToAction(nameof(Src));
        }

        var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read);
        return File(stream, "application/pdf", "MYGTS_SRC_Specification_V2.4.pdf");
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public IActionResult ViewSrcPdf()
    {
        var physicalPath = Path.Combine(_env.WebRootPath, "docs", "MYGTS_SRC_Specification.pdf");
        if (!System.IO.File.Exists(physicalPath))
        {
            return NotFound("SRC specification PDF not found.");
        }

        var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read);
        return File(stream, "application/pdf");
    }

    [HttpGet]
    [Authorize] // All authenticated users — content filtered per-role inside the view
    public IActionResult UserManual([FromQuery] string? branch)
    {
        if (!string.IsNullOrWhiteSpace(branch))
        {
            Response.Cookies.Append("mygts_branch", branch, new Microsoft.AspNetCore.Http.CookieOptions
            {
                Path = "/",
                MaxAge = System.TimeSpan.FromDays(365),
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                HttpOnly = false
            });
        }
        return View();
    }
}
