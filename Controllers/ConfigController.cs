using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Data;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Controllers;

[Authorize(Policy = "ExecutiveOnly")]
public sealed class ConfigController : Controller
{
    private readonly IAppDataStore _store;
    private readonly ApplicationDbContext _db;

    public ConfigController(IAppDataStore store, ApplicationDbContext db)
    {
        _store = store;
        _db = db;
    }

    private static readonly Dictionary<string, string> RtoDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PRICE_RECHARGE"] = "2000.00",
        ["PRICE_REACTIVATION"] = "3000.00",
        ["PRICE_TAX"] = "0.18",
        ["SIM_COST"] = "1300.00",
        ["SIM_COST_1_YEAR"] = "650.00",
        ["COMMISSION_COST"] = "500.00",
        ["REMINDER_DAYS"] = "30",
        ["SUSPENSION_DAYS"] = "30",
        ["TERMINATION_DAYS"] = "20",
        ["SENSORISE_SUSPENSION_DAYS"] = "30",
        ["SENSORISE_TERMINATION_DAYS"] = "20",
        ["ACTIVATION_EXPIRY_MONTHS"] = "11",
        ["ACTIVATION_EXPIRY_DAYS"] = "15",
        ["COMPANY_RECHARGE_RATE"] = "1298.00",
        ["COMPANY_REACTIVATION_RATE"] = "1534.00",
        ["BLUEMINE_RECHARGE_RATE"] = "1156.40",
        ["BLUEMINE_REACTIVATION_RATE"] = "1392.40"
    };

    private static readonly Dictionary<string, string> DmgDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PRICE_RECHARGE"] = "2500.00",
        ["PRICE_REACTIVATION"] = "3500.00",
        ["PRICE_TAX"] = "0.18",
        ["REMINDER_DAYS"] = "30",
        ["SUSPENSION_DAYS"] = "30",
        ["TERMINATION_DAYS"] = "20",
        ["SENSORISE_SUSPENSION_DAYS"] = "30",
        ["SENSORISE_TERMINATION_DAYS"] = "20",
        ["ACTIVATION_EXPIRY_MONTHS"] = "11",
        ["ACTIVATION_EXPIRY_DAYS"] = "15",
        ["COMPANY_RECHARGE_RATE"] = "1298.00",
        ["COMPANY_REACTIVATION_RATE"] = "1534.00",
        ["BLUEMINE_RECHARGE_RATE"] = "1156.40",
        ["BLUEMINE_REACTIVATION_RATE"] = "1392.40"
    };

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var defaults = currentBranch == "RTO" ? RtoDefaults : DmgDefaults;
        var data = await _store.SnapshotAsync();

        var merged = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        foreach (var item in data.Config)
        {
            if (!string.IsNullOrWhiteSpace(item.Value))
            {
                merged[item.Key] = item.Value;
            }
        }

        return View(new ConfigViewModel
        {
            Values = merged,
            IsEditingEnabled = false
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save(ConfigViewModel model)
    {
        if (!model.IsEditingEnabled)
        {
            TempData["Error"] = "Editing was not enabled. Enable the editing checkbox before saving configuration changes.";
            return RedirectToAction(nameof(Index));
        }

        var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        if (model.Values != null)
        {
            var existingItems = await _db.Config.ToListAsync();

            foreach (var pair in model.Values)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;

                // Protect against blanking values
                if (string.IsNullOrWhiteSpace(pair.Value)) continue;

                var item = existingItems.FirstOrDefault(config => string.Equals(config.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (item is null)
                {
                    _db.Config.Add(new ConfigItem
                    {
                        Key = pair.Key.Trim(),
                        Value = pair.Value.Trim(),
                        Branch = currentBranch
                    });
                }
                else
                {
                    item.Value = pair.Value.Trim();
                    item.Branch = currentBranch;
                }
            }

            await _db.SaveChangesAsync();
        }

        TempData["Success"] = "Configuration saved successfully.";
        return RedirectToAction(nameof(Index));
    }
}
