using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Areas.RTO.Controllers;
using Microsoft.AspNetCore.Mvc;

[Area("RTO")]
[Authorize]
[RequireModulePermission("Reminders", PermissionLevel.View)]
public sealed class RemindersController : Controller
{
    private readonly IAppDataStore _store;

    public RemindersController(IAppDataStore store) => _store = store;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var data = await _store.SnapshotAsync();
        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        // Audit processed unpaid recharges against transactions by ICCID/IMEI/VehicleNo
        var auditChanges = RechargeAuditHelper.AuditProcessedRechargePayments(data, branch);
        if (auditChanges > 0)
        {
            await _store.UpdateAsync(d => RechargeAuditHelper.AuditProcessedRechargePayments(d, branch));
            data = await _store.SnapshotAsync();
        }

        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var d in data.Devices)
        {
            DeviceLifecycleHelper.SyncDevice(d, config);
        }

        var devices = data.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.VehicleNo) && !string.Equals(device.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase) && !device.IsNotInUse)
            .ToList();
        var today = DateTime.UtcNow.Date;

        var buckets = new Dictionary<string, IReadOnlyList<DeviceRecord>>
        {
            ["Suspension Window"] = devices.Where(device => string.Equals(device.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase)).ToList(),
            ["Termination Window"] = devices.Where(device => string.Equals(device.LifecycleStatus, "Terminated", StringComparison.OrdinalIgnoreCase)).ToList(),
            ["Dead / Deactivated"] = devices.Where(device => string.Equals(device.LifecycleStatus, "Dead", StringComparison.OrdinalIgnoreCase)).ToList(),
            ["Expiring This Week"] = devices.Where(device => DateValue.DaysFromNow(device.LatestExpiryDate) is >= 0 and <= 7).ToList(),
            ["Unpaid Vehicles"] = devices.Where(device => !string.IsNullOrWhiteSpace(device.VehicleNo) 
                && !device.ExcludeFromUnpaid 
                && !string.Equals(device.Paid, "Yes", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(device.Paid, "No", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(device.InvoiceId) || !device.InvoiceId.StartsWith("old", StringComparison.OrdinalIgnoreCase))
                && !data.Transactions.Any(t => string.Equals(t.VehicleNo, device.VehicleNo, StringComparison.OrdinalIgnoreCase))).ToList()
        };

        var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
        ViewBag.UrgentDevices = devices.Where(device => DateValue.DaysFromNow(device.LatestExpiryDate) is >= 0 and <= 1 && (string.IsNullOrWhiteSpace(device.InformedDate) || device.InformedDate != todayStr)).ToList();
        
        var visibleReminders = data.ManualReminders
            .Where(item => User.CanViewReminder(item))
            .ToList();

        ViewBag.CompletedCount = visibleReminders.Count(r => r.IsComplete);
        ViewBag.VendorPhoneMap = data.Vendors
            .Where(v => !string.IsNullOrWhiteSpace(v.Phone))
            .GroupBy(v => v.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Phone.Trim(), StringComparer.OrdinalIgnoreCase);

        ViewBag.Usernames = data.Users
            .Where(u => User.CanViewUser(u))
            .Select(u => !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName : u.Email)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(u => u)
            .ToList();

        var activeReminders = visibleReminders
            .OrderBy(item => item.IsComplete)
            .ThenBy(item => DateValue.Parse(item.DueDate))
            .ToList();

        var branchRecharges = (data.Recharges ?? new List<RechargeRecord>())
            .Where(r => string.Equals(r.Branch, branch, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pendingRecharges = branchRecharges
            .Where(r => string.Equals(r.RechargeStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CreatedAt)
            .ToList();

        var unpaidRecharges = branchRecharges
            .Where(r => string.Equals(r.RechargeStatus, "Processed", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(r.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.RechargeDate)
            .ToList();

        return View(new ReminderPageViewModel 
        { 
            Buckets = buckets, 
            ManualReminders = activeReminders,
            PendingRecharges = pendingRecharges,
            UnpaidRecharges = unpaidRecharges
        });
    }

    [HttpGet]
    public async Task<IActionResult> Completed()
    {
        var data = await _store.SnapshotAsync();
        var completedReminders = data.ManualReminders
            .Where(item => item.IsComplete && User.CanViewReminder(item))
            .OrderByDescending(item => DateValue.Parse(item.DueDate))
            .ToList();
        return View(completedReminders);
    }

    [HttpPost]
    [RequireModulePermission("Reminders", PermissionLevel.Edit)]
    public async Task<IActionResult> Create(ReminderViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            TempData["Error"] = "Reminder title is required.";
            return RedirectToAction(nameof(Index));
        }
        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var creator = User.Identity?.Name ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "Team";
        var dueDateStr = model.DueDate.HasValue ? model.DueDate.Value.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");

        await _store.UpdateAsync(data => data.ManualReminders.Add(new ManualReminder { 
            Title = model.Title.Trim(), 
            Details = (model.Details ?? "").Trim(), 
            DueDate = dueDateStr,
            Branch = branch,
            CreatedBy = creator,
            TargetAudience = string.IsNullOrWhiteSpace(model.TargetAudience) ? "@all" : model.TargetAudience.Trim()
        }));
        TempData["Success"] = "Reminder added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Reminders", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(ReminderViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id) || string.IsNullOrWhiteSpace(model.Title))
        {
            TempData["Error"] = "Reminder title is required.";
            return RedirectToAction(nameof(Index));
        }

        var dueDateStr = model.DueDate.HasValue 
            ? model.DueDate.Value.ToString("yyyy-MM-dd") 
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

        await _store.UpdateAsync(data =>
        {
            var item = data.ManualReminders.FirstOrDefault(r => r.Id == model.Id);
            if (item != null)
            {
                item.Title = model.Title.Trim();
                item.Details = (model.Details ?? "").Trim();
                item.DueDate = dueDateStr;
                item.TargetAudience = string.IsNullOrWhiteSpace(model.TargetAudience) ? "@all" : model.TargetAudience.Trim();
            }
        });

        TempData["Success"] = "Follow-up note updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Reminders", PermissionLevel.Edit)]
    public async Task<IActionResult> Complete(string id, string? returnUrl = null)
    {
        await _store.UpdateAsync(data => { var item = data.ManualReminders.FirstOrDefault(reminder => reminder.Id == id); if (item is not null) item.IsComplete = !item.IsComplete; });
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Reminders", PermissionLevel.Edit)]
    public async Task<IActionResult> Delete(string id, string? returnUrl = null)
    {
        await _store.UpdateAsync(data => data.ManualReminders.RemoveAll(item => item.Id == id));
        TempData["Success"] = "Follow-up note deleted.";
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }
}
