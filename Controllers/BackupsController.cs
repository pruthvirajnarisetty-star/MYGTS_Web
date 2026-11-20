using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using MygtsMvc.Data;
using MygtsMvc.Models;
using MygtsMvc.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MygtsMvc.Controllers;

[Authorize]
[RequireModulePermission("Dashboard", PermissionLevel.View)] 
public class BackupsController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly ApplicationDbContext _dbContext;

    public BackupsController(IWebHostEnvironment env, ApplicationDbContext dbContext)
    {
        _env = env;
        _dbContext = dbContext;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var rootDir = Path.Combine(_env.ContentRootPath, "Data", "backups");
        var batches = new Dictionary<string, List<FileInfo>>();

        var categories = new[] { "Today", "Daily", "Weekly", "Monthly", "Yearly" };
        
        foreach (var category in categories)
        {
            var dir = Path.Combine(rootDir, category);
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "*.db")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();
                batches[category] = files;
            }
            else
            {
                batches[category] = new List<FileInfo>();
            }
        }

        return View(batches);
    }

    [HttpGet]
    public IActionResult Download(string category, string filename)
    {
        // Sanitize inputs
        category = Path.GetFileName(category);
        filename = Path.GetFileName(filename);

        var filepath = Path.Combine(_env.ContentRootPath, "Data", "backups", category, filename);
        if (!System.IO.File.Exists(filepath))
        {
            return NotFound();
        }

        return PhysicalFile(filepath, "application/octet-stream", filename);
    }

    [HttpPost]
    public async Task<IActionResult> RunSqliteBackup()
    {
        try
        {
            var directory = Path.Combine(_env.ContentRootPath, "Data", "backups", "Today");
            Directory.CreateDirectory(directory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"backup_{timestamp}.db";
            var fullPath = Path.Combine(directory, fileName);
            var sqlitePath = fullPath.Replace("\\", "/");

#pragma warning disable EF1002
            await _dbContext.Database.ExecuteSqlRawAsync($"VACUUM INTO '{sqlitePath}'");
#pragma warning restore EF1002

            TempData["Success"] = $"Database backup created successfully: {fileName}";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Backup failed: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("seed-reminders")]
    [AllowAnonymous]
    public async Task<IActionResult> SeedReminders()
    {
        var today = DateTime.UtcNow.Date;
        var devices = new List<DeviceRecord>
        {
            new DeviceRecord { VehicleNo = "TEST-SUSPENSION-1", LatestExpiryDate = today.ToString("yyyy-MM-dd"), LifecycleStatus = "Active", Vendor = "Test Vendor" },
            new DeviceRecord { VehicleNo = "TEST-TOMORROW-1", LatestExpiryDate = today.AddDays(1).ToString("yyyy-MM-dd"), Vendor = "Test Vendor" },
            new DeviceRecord { VehicleNo = "TEST-NEAR-DEAD-1", LatestExpiryDate = today.AddDays(-15).ToString("yyyy-MM-dd"), Vendor = "Test Vendor" },
            new DeviceRecord { VehicleNo = "TEST-EXPIRED-1", LatestExpiryDate = today.AddDays(-60).ToString("yyyy-MM-dd"), Vendor = "Test Vendor" },
            new DeviceRecord { VehicleNo = "TEST-THIS-WEEK-1", LatestExpiryDate = today.AddDays(5).ToString("yyyy-MM-dd"), Vendor = "Test Vendor" },
            new DeviceRecord { VehicleNo = "TEST-NO-OWNER-1", LatestExpiryDate = today.AddDays(100).ToString("yyyy-MM-dd"), Owner = "", Vendor = "Test Vendor" }
        };

        _dbContext.Devices.AddRange(devices);
        await _dbContext.SaveChangesAsync();

        return Content("Reminders Seeded Successfully!");
    }
}
