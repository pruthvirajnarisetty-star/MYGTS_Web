using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;

namespace MygtsMvc.Controllers;

[Authorize]
public sealed class AlertsController : Controller
{
    private readonly IAppDataStore _store;

    public AlertsController(IAppDataStore store) => _store = store;

    private static string ResolveVendorPhone(string vendorName, IReadOnlyList<VendorRecord> vendors, IReadOnlyList<DeviceRecord> devices, string branch)
    {
        if (string.IsNullOrWhiteSpace(vendorName)) return "";
        var cleanName = vendorName.Trim();

        // 1. Direct match in Vendors table for current branch, then across all branches
        var v = vendors.FirstOrDefault(x => string.Equals(x.Branch, branch, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase))
             ?? vendors.FirstOrDefault(x => string.Equals(x.Name?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase));

        if (v != null)
        {
            var p = v.EffectiveWhatsAppNumber;
            if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            if (!string.IsNullOrWhiteSpace(v.WhatsAppNumber)) return v.WhatsAppNumber.Trim();
            if (!string.IsNullOrWhiteSpace(v.Phone)) return v.Phone.Trim();
            if (!string.IsNullOrWhiteSpace(v.AltPhone)) return v.AltPhone.Trim();
            if (!string.IsNullOrWhiteSpace(v.ExtraPhones))
            {
                var ep = v.ExtraPhones.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ep)) return ep.Trim();
            }
        }

        // 2. Fallback to most recent CellNo from Devices table for this vendor
        var devCell = devices
            .Where(d => (string.Equals(d.Branch, branch, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(branch))
                        && string.Equals(d.Vendor?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(d.CellNo))
            .OrderByDescending(d => d.LatestExpiryDate)
            .Select(d => d.CellNo!.Trim())
            .FirstOrDefault()
            ?? devices
                .Where(d => string.Equals(d.Vendor?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(d.CellNo))
                .OrderByDescending(d => d.LatestExpiryDate)
                .Select(d => d.CellNo!.Trim())
                .FirstOrDefault();

        return devCell ?? "";
    }

    [HttpGet]
    public async Task<IActionResult> GetExpiringVendorsSummary(string? branch = null)
    {
        var data = await _store.SnapshotAsync();
        var currentBranch = branch ?? (Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG");

        var branchDevices = data.Devices
            .Where(d => string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(d.VehicleNo)
                        && !d.IsNotInUse
                        && !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var registeredVendors = data.Vendors
            .Where(v => string.Equals(v.Branch, currentBranch, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v.Name))
            .Select(v => v.Name.Trim())
            .ToList();

        var deviceVendors = branchDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.Vendor))
            .Select(d => d.Vendor!.Trim())
            .ToList();

        var allVendorNames = registeredVendors
            .Concat(deviceVendors)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        var result = allVendorNames.Select(name =>
        {
            var phone = ResolveVendorPhone(name, data.Vendors, branchDevices, currentBranch);
            var devList = branchDevices.Where(d => string.Equals(d.Vendor?.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();
            var expiringCount = devList.Count(d => 
                DateValue.DaysFromNow(d.LatestExpiryDate) is >= 0 and <= 30
                || (DateValue.DaysFromNow(d.LatestExpiryDate) < 0 && (d.DaysPastExpiry ?? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value) <= 60)
                || string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase));

            return new
            {
                vendor = name,
                expiringCount = expiringCount,
                totalCount = devList.Count,
                phone = phone
            };
        })
        .OrderByDescending(v => v.expiringCount > 0 ? 1 : 0)
        .ThenByDescending(v => v.expiringCount)
        .ThenByDescending(v => v.totalCount)
        .ThenBy(v => v.vendor)
        .ToList();

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetVendorExpiringVehicles(string vendor, string? branch = null)
    {
        if (string.IsNullOrWhiteSpace(vendor)) return BadRequest();
        var data = await _store.SnapshotAsync();
        var currentBranch = branch ?? (Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG");
        var vendorClean = vendor.Trim();

        var branchDevices = data.Devices
            .Where(d => string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(d.VehicleNo)
                        && !d.IsNotInUse
                        && string.Equals(d.Vendor?.Trim(), vendorClean, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var primaryList = branchDevices
            .Where(d => DateValue.DaysFromNow(d.LatestExpiryDate) is >= 0 and <= 30
                        || (DateValue.DaysFromNow(d.LatestExpiryDate) < 0 && (d.DaysPastExpiry ?? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value) <= 60)
                        || string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => DateValue.DaysFromNow(d.LatestExpiryDate) ?? 9999)
            .ToList();

        var devicesToReturn = (primaryList.Count > 0 ? primaryList : branchDevices.OrderBy(d => d.LatestExpiryDate).Take(50).ToList())
            .Select(d => {
                var daysLeft = DateValue.DaysFromNow(d.LatestExpiryDate) ?? 9999;
                return new
                {
                    vehicleNo = d.VehicleNo,
                    owner = d.Owner ?? "",
                    cellNo = d.CellNo ?? "",
                    expiryDate = DateValue.Short(d.LatestExpiryDate),
                    daysLeft = daysLeft,
                    isExpiringSoon = daysLeft is >= 0 and <= 7,
                    isOverdue = daysLeft < 0,
                    isUrgent = daysLeft is >= 0 and <= 1 || daysLeft < 0,
                    status = !string.IsNullOrWhiteSpace(d.LifecycleStatus) ? d.LifecycleStatus : (!string.IsNullOrWhiteSpace(d.Status) ? d.Status : "Active")
                };
            })
            .ToList();

        var phone = ResolveVendorPhone(vendorClean, data.Vendors, data.Devices, currentBranch);
        var vRec = data.Vendors.FirstOrDefault(v => string.Equals(v.Name.Trim(), vendorClean, StringComparison.OrdinalIgnoreCase));

        return Json(new
        {
            vendor = vendorClean,
            phone = phone,
            altPhone = vRec?.AltPhone ?? "",
            devices = devicesToReturn
        });
    }
}

