using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Data;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Areas.RTO.Controllers;
using Microsoft.AspNetCore.Mvc;

[Area("RTO")]
[Authorize(Policy = "AdminOnly")]
public sealed class ConfigController : Controller
{
    private readonly IAppDataStore _store;
    private readonly ApplicationDbContext _db;

    public ConfigController(IAppDataStore store, ApplicationDbContext db)
    {
        _store = store;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var defaults = currentBranch == "RTO" ? DeviceLifecycleHelper.RtoDefaults : DeviceLifecycleHelper.DmgDefaults;
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
            var existingItems = await _db.Config.IgnoreQueryFilters().Where(c => c.Branch == currentBranch).ToListAsync();

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

            // Dynamic Synchronization:
            // When configuration changes (activation expiry period, suspension days, termination days),
            // dynamically synchronize all devices in this branch with the new config.
            var updatedConfigItems = await _db.Config.IgnoreQueryFilters().Where(c => c.Branch == currentBranch).ToListAsync();
            var mergedConfig = DeviceLifecycleHelper.BuildMergedConfig(currentBranch, updatedConfigItems);

            var branchDevices = await _db.Devices.IgnoreQueryFilters().Where(d => d.Branch == currentBranch).ToListAsync();
            foreach (var dev in branchDevices)
            {
                DeviceLifecycleHelper.SyncDevice(dev, mergedConfig);
            }
            await _db.SaveChangesAsync();

            // Dynamically recalculate all existing transactions for this branch
            var branchTransactions = await _db.Transactions.IgnoreQueryFilters().Where(t => t.Branch == currentBranch).ToListAsync();
            foreach (var txn in branchTransactions)
            {
                if (!string.IsNullOrWhiteSpace(txn.ActivationDate))
                {
                    var isAct = string.Equals(txn.PaymentFor, "Activation", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(txn.PaymentFor, "NEW DEVICE", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(txn.PaymentFor, "New", StringComparison.OrdinalIgnoreCase);
                    if (isAct)
                    {
                        var newExp = DeviceLifecycleHelper.CalculateExpiryFromActivation(txn.ActivationDate, currentBranch, txn.Type, mergedConfig);
                        if (!string.IsNullOrWhiteSpace(newExp))
                        {
                            txn.NewExpiryDate = newExp;
                        }
                    }
                }
            }
            await _db.SaveChangesAsync();

            // Synchronize in-memory snapshot store as well
            await _store.UpdateAsync(data =>
            {
                if (model.Values != null)
                {
                    foreach (var pair in model.Values)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
                        var cItem = data.Config.FirstOrDefault(c => string.Equals(c.Key, pair.Key, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Branch, currentBranch, StringComparison.OrdinalIgnoreCase));
                        if (cItem != null)
                        {
                            cItem.Value = pair.Value.Trim();
                        }
                        else
                        {
                            data.Config.Add(new ConfigItem
                            {
                                Key = pair.Key.Trim(),
                                Value = pair.Value.Trim(),
                                Branch = currentBranch
                            });
                        }
                    }
                }

                var storeDevices = data.Devices.Where(d => string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase));
                foreach (var dev in storeDevices)
                {
                    DeviceLifecycleHelper.SyncDevice(dev, mergedConfig);
                }

                var storeTxns = data.Transactions.Where(t => string.Equals(t.Branch, currentBranch, StringComparison.OrdinalIgnoreCase));
                foreach (var txn in storeTxns)
                {
                    if (!string.IsNullOrWhiteSpace(txn.ActivationDate))
                    {
                        var isAct = string.Equals(txn.PaymentFor, "Activation", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(txn.PaymentFor, "NEW DEVICE", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(txn.PaymentFor, "New", StringComparison.OrdinalIgnoreCase);
                        if (isAct)
                        {
                            var newExp = DeviceLifecycleHelper.CalculateExpiryFromActivation(txn.ActivationDate, currentBranch, txn.Type, mergedConfig);
                            if (!string.IsNullOrWhiteSpace(newExp))
                            {
                                txn.NewExpiryDate = newExp;
                            }
                        }
                    }
                }
            });
        }

        TempData["Success"] = "Configuration saved successfully. Expiry dates and lifecycle statuses updated dynamically.";
        return RedirectToAction(nameof(Index));
    }
}
