using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Areas.DMG.Controllers;

[Area("DMG")]
[Authorize]
public sealed class SensoriseController : Controller
{
    private readonly IAppDataStore _store;
    private readonly SpreadsheetService _spreadsheets;

    public SensoriseController(IAppDataStore store, SpreadsheetService spreadsheets)
    {
        _store = store;
        _spreadsheets = spreadsheets;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? query = null, string? statusFilter = null, int page = 1, int pageSize = 50)
    {
        var data = await _store.SnapshotAsync();
        var configMap = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var d in data.Devices)
        {
            DeviceLifecycleHelper.SyncDevice(d, configMap);
        }

        // Hardcoded: Only DMG branch devices where NetworkOperator is Sensorise
        var sensoriseFleet = data.Devices
            .Where(d => string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d.NetworkOperator, "Sensorise", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(d.VehicleNo) &&
                        !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int suspensionDays = 30;
        int terminationDays = 20;
        if (configMap.TryGetValue("SENSORISE_SUSPENSION_DAYS", out var ssStr) && int.TryParse(ssStr, out var ssVal))
            suspensionDays = ssVal;
        else if (configMap.TryGetValue("SUSPENSION_DAYS", out var sStr) && int.TryParse(sStr, out var sVal))
            suspensionDays = sVal;

        if (configMap.TryGetValue("SENSORISE_TERMINATION_DAYS", out var stStr) && int.TryParse(stStr, out var stVal))
            terminationDays = stVal;
        else if (configMap.TryGetValue("TERMINATION_DAYS", out var tStr) && int.TryParse(tStr, out var tVal))
            terminationDays = tVal;

        int deadThreshold = suspensionDays + terminationDays;

        var allItems = new List<SensoriseDeviceItem>();
        int countSuspension = 0;
        int countTerminated = 0;
        int countRecentlyDead = 0;

        foreach (var d in sensoriseFleet)
        {
            var daysPast = d.DaysPastExpiry ?? (DateValue.DaysFromNow(d.LatestExpiryDate).HasValue ? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value : 0);
            var isSuspension = string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase);
            var isTerminated = string.Equals(d.LifecycleStatus, "Terminated", StringComparison.OrdinalIgnoreCase);
            var isDead = string.Equals(d.LifecycleStatus, "Dead", StringComparison.OrdinalIgnoreCase);

            // Devices that changed to dead status are shown for 7 days (1 week from the change into dead status)
            var isRecentlyDead = isDead && daysPast > deadThreshold && daysPast <= (deadThreshold + 7);

            if (isSuspension) countSuspension++;
            if (isTerminated) countTerminated++;
            if (isRecentlyDead) countRecentlyDead++;

            if (isSuspension || isTerminated || isRecentlyDead)
            {
                int deadDaysRemaining = isRecentlyDead ? Math.Max(1, (deadThreshold + 7) - daysPast + 1) : 0;
                string badgeClass = isSuspension ? "suspension" : (isTerminated ? "terminated" : "dead");

                allItems.Add(new SensoriseDeviceItem
                {
                    Device = d,
                    LifecycleStatus = d.LifecycleStatus,
                    DaysPastExpiry = daysPast,
                    IsRecentlyDead = isRecentlyDead,
                    DeadDaysRemaining = deadDaysRemaining,
                    StatusBadgeClass = badgeClass
                });
            }
        }

        // Apply Status Filter
        var filtered = allItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(statusFilter) && !string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(statusFilter, "Suspension", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(x => string.Equals(x.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase));
            else if (string.Equals(statusFilter, "Terminated", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(x => string.Equals(x.LifecycleStatus, "Terminated", StringComparison.OrdinalIgnoreCase));
            else if (string.Equals(statusFilter, "Dead", StringComparison.OrdinalIgnoreCase) || string.Equals(statusFilter, "RecentlyDead", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(x => x.IsRecentlyDead);
        }

        // Apply Search Query
        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filtered = filtered.Where(x => terms.All(t =>
                (x.Device.VehicleNo != null && x.Device.VehicleNo.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (x.Device.Imei != null && x.Device.Imei.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (x.Device.Iccid != null && x.Device.Iccid.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (x.Device.Owner != null && x.Device.Owner.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (x.Device.Vendor != null && x.Device.Vendor.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (x.Device.CellNo != null && x.Device.CellNo.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (x.Device.SimAirtel != null && x.Device.SimAirtel.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (x.Device.SimBsnl != null && x.Device.SimBsnl.Contains(t, StringComparison.OrdinalIgnoreCase))));
        }

        var totalItems = filtered.Count();
        if (page < 1) page = 1;

        var ordered = filtered.OrderByDescending(x => x.DaysPastExpiry);
        var pagedItems = (pageSize >= 1000000 || pageSize <= 0)
            ? ordered.ToList()
            : ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var vm = new SensoriseViewModel
        {
            Query = query,
            StatusFilter = statusFilter ?? "All",
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            SuspensionCount = countSuspension,
            TerminatedCount = countTerminated,
            RecentlyDeadCount = countRecentlyDead,
            Devices = pagedItems
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ExportExcel(string? query = null, string? statusFilter = null)
    {
        var data = await _store.SnapshotAsync();
        var configMap = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var d in data.Devices)
        {
            DeviceLifecycleHelper.SyncDevice(d, configMap);
        }

        var sensoriseFleet = data.Devices
            .Where(d => string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d.NetworkOperator, "Sensorise", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(d.VehicleNo) &&
                        !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        int suspensionDays = 30;
        int terminationDays = 20;
        if (configMap.TryGetValue("SENSORISE_SUSPENSION_DAYS", out var ssStr) && int.TryParse(ssStr, out var ssVal))
            suspensionDays = ssVal;
        else if (configMap.TryGetValue("SUSPENSION_DAYS", out var sStr) && int.TryParse(sStr, out var sVal))
            suspensionDays = sVal;

        if (configMap.TryGetValue("SENSORISE_TERMINATION_DAYS", out var stStr) && int.TryParse(stStr, out var stVal))
            terminationDays = stVal;
        else if (configMap.TryGetValue("TERMINATION_DAYS", out var tStr) && int.TryParse(tStr, out var tVal))
            terminationDays = tVal;

        int deadThreshold = suspensionDays + terminationDays;

        var matching = new List<DeviceRecord>();

        foreach (var d in sensoriseFleet)
        {
            var daysPast = d.DaysPastExpiry ?? (DateValue.DaysFromNow(d.LatestExpiryDate).HasValue ? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value : 0);
            var isSuspension = string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase);
            var isTerminated = string.Equals(d.LifecycleStatus, "Terminated", StringComparison.OrdinalIgnoreCase);
            var isDead = string.Equals(d.LifecycleStatus, "Dead", StringComparison.OrdinalIgnoreCase);
            var isRecentlyDead = isDead && daysPast > deadThreshold && daysPast <= (deadThreshold + 7);

            if (isSuspension || isTerminated || isRecentlyDead)
            {
                matching.Add(d);
            }
        }

        var filtered = matching.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(statusFilter) && !string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(statusFilter, "Suspension", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(d => string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase));
            else if (string.Equals(statusFilter, "Terminated", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(d => string.Equals(d.LifecycleStatus, "Terminated", StringComparison.OrdinalIgnoreCase));
            else if (string.Equals(statusFilter, "Dead", StringComparison.OrdinalIgnoreCase) || string.Equals(statusFilter, "RecentlyDead", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(d => {
                    var daysPast = d.DaysPastExpiry ?? 0;
                    return string.Equals(d.LifecycleStatus, "Dead", StringComparison.OrdinalIgnoreCase) && daysPast > deadThreshold && daysPast <= (deadThreshold + 7);
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filtered = filtered.Where(d => terms.All(t =>
                (d.VehicleNo != null && d.VehicleNo.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (d.Imei != null && d.Imei.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (d.Iccid != null && d.Iccid.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (d.Owner != null && d.Owner.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (d.Vendor != null && d.Vendor.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
                (d.CellNo != null && d.CellNo.Contains(t, StringComparison.OrdinalIgnoreCase))));
        }

        var exportDevices = filtered.OrderByDescending(d => d.DaysPastExpiry ?? 0).ToList();
        var bytes = _spreadsheets.ExportDevices(exportDevices, "DMG");
        var fileName = $"sensorise-dmg-devices-{DateTime.UtcNow:yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
