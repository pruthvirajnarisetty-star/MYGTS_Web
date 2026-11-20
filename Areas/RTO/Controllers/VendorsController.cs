using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Data;
using MygtsMvc.Models;
using MygtsMvc.Services;

namespace MygtsMvc.Areas.RTO.Controllers;
using MygtsMvc.Controllers;

[Area("RTO")]
[Authorize]
[RequireModulePermission("Vendors", PermissionLevel.View)]
public sealed class VendorsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly SpreadsheetService _spreadsheets;

    public VendorsController(ApplicationDbContext context, SpreadsheetService spreadsheets)
    {
        _context = context;
        _spreadsheets = spreadsheets;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? sortOrder)
    {
        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        // Fast batch collection of vendors from Devices that are not yet in Vendors table
        var existingNames = await _context.Vendors
            .Where(v => v.Branch == branch)
            .Select(v => v.Name.Trim().ToLower())
            .ToListAsync();
        var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        var deviceVendorNames = await _context.Devices
            .Where(d => d.Branch == branch && !string.IsNullOrWhiteSpace(d.Vendor))
            .Select(d => d.Vendor.Trim())
            .Distinct()
            .ToListAsync();

        var missing = deviceVendorNames
            .Where(name => !string.IsNullOrWhiteSpace(name) && !existingSet.Contains(name))
            .Select(name => new VendorRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Phone = "",
                AltPhone = "",
                Notes = "",
                Branch = branch,
                CreatedAt = DateTime.UtcNow.ToString("O")
            })
            .ToList();

        if (missing.Count > 0)
        {
            await _context.Vendors.AddRangeAsync(missing);
            await _context.SaveChangesAsync();
        }


        // Fetch all vendors for current branch ordered by name
        var vendors = await _context.Vendors
            .Where(v => v.Branch == branch)
            .OrderBy(v => v.Name)
            .ToListAsync();

        // Device count per vendor for badges (group case-insensitively in memory to prevent duplicate key exception)
        var deviceVendorList = await _context.Devices
            .Where(d => d.Branch == branch && !string.IsNullOrWhiteSpace(d.Vendor))
            .Select(d => d.Vendor.Trim())
            .ToListAsync();

        var deviceCountMap = deviceVendorList
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        ViewBag.DeviceCountMap = deviceCountMap;

        // Leaderboard: top 3 vendors by device count
        ViewBag.Leaderboard = deviceCountMap
            .OrderByDescending(kvp => kvp.Value)
            .Take(3)
            .ToList();

        // Sorting
        if (sortOrder == "count_asc")
        {
            vendors = vendors.OrderBy(v => deviceCountMap.TryGetValue(v.Name.Trim(), out var count) ? count : 0).ThenBy(v => v.Name).ToList();
        }
        else if (sortOrder == "count_desc")
        {
            vendors = vendors.OrderByDescending(v => deviceCountMap.TryGetValue(v.Name.Trim(), out var count) ? count : 0).ThenBy(v => v.Name).ToList();
        }
        else if (sortOrder == "name_desc")
        {
            vendors = vendors.OrderByDescending(v => v.Name).ToList();
        }
        else
        {
            vendors = vendors.OrderBy(v => v.Name).ToList();
        }

        ViewBag.CurrentSort = sortOrder ?? "";
        ViewBag.CountSortParam = sortOrder == "count_desc" ? "count_asc" : "count_desc";

        ViewBag.Branch = branch;
        return View(vendors);
    }

    [HttpPost]
    [RequireModulePermission("Vendors", PermissionLevel.Edit)]
    public async Task<IActionResult> SavePhone(string id, string phone, string altPhone, string notes, string? batchPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();

        var vendor = await _context.Vendors.FirstOrDefaultAsync(v => v.Id == id);
        if (vendor == null) return NotFound();

        vendor.Phone = phone?.Trim() ?? "";
        vendor.AltPhone = altPhone?.Trim() ?? "";
        vendor.Notes = notes?.Trim() ?? "";
        if (batchPrefix != null) vendor.BatchPrefix = batchPrefix.Trim().ToUpperInvariant();
        await _context.SaveChangesAsync();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
            Request.Headers.Accept.ToString().Contains("application/json"))
        {
            var cleanPhone = new string(vendor.Phone.Where(char.IsDigit).ToArray());
            if (cleanPhone.Length == 10) cleanPhone = "91" + cleanPhone;
            var waUrl = !string.IsNullOrWhiteSpace(cleanPhone)
                ? $"https://web.whatsapp.com/send?phone={cleanPhone}"
                : "";

            return Json(new
            {
                success = true,
                id = vendor.Id,
                name = vendor.Name,
                batchPrefix = vendor.BatchPrefix,
                effectivePrefix = vendor.EffectiveBatchPrefix,
                phone = vendor.Phone,
                altPhone = vendor.AltPhone,
                notes = vendor.Notes,
                waUrl
            });
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Vendors", PermissionLevel.Edit)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateVendorProfileRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Id))
            return BadRequest(new { success = false, message = "Invalid request ID." });

        var vendor = await _context.Vendors.FirstOrDefaultAsync(v => v.Id == request.Id);
        if (vendor == null)
            return NotFound(new { success = false, message = "Vendor not found." });

        var oldName = vendor.Name.Trim();
        var newName = !string.IsNullOrWhiteSpace(request.Name) ? request.Name.Trim() : oldName;

        vendor.Name = newName;
        vendor.BatchPrefix = request.BatchPrefix?.Trim().ToUpperInvariant() ?? "";
        vendor.Phone = request.Phone?.Trim() ?? "";
        vendor.AltPhone = request.AltPhone?.Trim() ?? "";
        vendor.ExtraPhones = request.ExtraPhones?.Trim() ?? "";
        vendor.WhatsAppSameAsPhone = request.WhatsAppSameAsPhone;
        vendor.WhatsAppNumber = request.WhatsAppSameAsPhone ? vendor.Phone : (request.WhatsAppNumber?.Trim() ?? "");
        vendor.Notes = request.Notes?.Trim() ?? "";

        // If vendor name was changed, cascade to devices and transactions in this branch
        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            var devicesToUpdate = await _context.Devices
                .Where(d => d.Branch == vendor.Branch && d.Vendor == oldName)
                .ToListAsync();
            foreach (var d in devicesToUpdate) d.Vendor = newName;

            var txToUpdate = await _context.Transactions
                .Where(t => t.Branch == vendor.Branch && t.Vendor == oldName)
                .ToListAsync();
            foreach (var t in txToUpdate) t.Vendor = newName;
        }

        await _context.SaveChangesAsync();

        var effectivePrefix = vendor.EffectiveBatchPrefix;
        var effectiveWa = !string.IsNullOrWhiteSpace(vendor.WhatsAppNumber) ? vendor.WhatsAppNumber : vendor.Phone;
        var cleanPhone = new string(effectiveWa.Where(char.IsDigit).ToArray());
        if (cleanPhone.Length == 10) cleanPhone = "91" + cleanPhone;
        var waUrl = !string.IsNullOrWhiteSpace(cleanPhone)
            ? $"https://web.whatsapp.com/send?phone={cleanPhone}"
            : "";

        return Json(new
        {
            success = true,
            id = vendor.Id,
            name = vendor.Name,
            batchPrefix = vendor.BatchPrefix,
            effectivePrefix,
            phone = vendor.Phone,
            altPhone = vendor.AltPhone,
            extraPhones = vendor.ExtraPhones,
            whatsAppNumber = vendor.WhatsAppNumber,
            whatsAppSameAsPhone = vendor.WhatsAppSameAsPhone,
            notes = vendor.Notes,
            waUrl
        });
    }

    [HttpPost]
    [RequireModulePermission("Vendors", PermissionLevel.Edit)]
    public async Task<IActionResult> AddVendor(
        string name, 
        string? phone = null, 
        string? altPhone = null, 
        string? batchPrefix = null, 
        string? extraPhones = null, 
        string? whatsAppNumber = null, 
        bool whatsAppSameAsPhone = true, 
        string? notes = null)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
            var trimmed = name.Trim();
            var exists = await _context.Vendors.AnyAsync(v => v.Branch == branch && v.Name.ToLower() == trimmed.ToLower());
            if (!exists)
            {
                var newVendor = new VendorRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = trimmed,
                    Phone = phone?.Trim() ?? "",
                    AltPhone = altPhone?.Trim() ?? "",
                    BatchPrefix = batchPrefix?.Trim().ToUpperInvariant() ?? "",
                    ExtraPhones = extraPhones?.Trim() ?? "",
                    WhatsAppNumber = whatsAppSameAsPhone ? "" : (whatsAppNumber?.Trim() ?? ""),
                    WhatsAppSameAsPhone = whatsAppSameAsPhone,
                    Notes = notes?.Trim() ?? "",
                    Branch = branch,
                    CreatedAt = DateTime.UtcNow.ToString("O")
                };
                _context.Vendors.Add(newVendor);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Vendor '{trimmed}' added successfully.";
            }
            else
            {
                TempData["Error"] = $"Vendor '{trimmed}' already exists.";
            }
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Vendors", PermissionLevel.Edit)]
    public async Task<IActionResult> Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();

        var vendor = await _context.Vendors.FirstOrDefaultAsync(v => v.Id == id);
        if (vendor != null)
        {
            _context.Vendors.Remove(vendor);
            await _context.SaveChangesAsync();
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
            Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return Json(new { success = true });
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequireModulePermission("Vendors", PermissionLevel.View)]
    public async Task<IActionResult> Profile(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();

        var vendor = await _context.Vendors.FirstOrDefaultAsync(v => v.Id == id);
        if (vendor == null) return NotFound();

        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        var vendorNameLower = vendor.Name.Trim().ToLower();

        var allDevices = await _context.Devices
            .Where(d => d.Branch == branch && d.Vendor != null && d.Vendor != "")
            .ToListAsync();

        var vendorDevices = allDevices
            .Where(d => d.Vendor.Trim().Equals(vendor.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var allTransactions = await _context.Transactions
            .Where(t => t.Branch == branch && t.Vendor != null && t.Vendor != "")
            .ToListAsync();

        var vendorTransactions = allTransactions
            .Where(t => t.Vendor.Trim().Equals(vendor.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Calculate unpaid devices
        // Assuming a device is unpaid if it's not excluded and has no matching transaction by VehicleNo
        // Using same logic as DashboardService (ignoring replaced devices)
        var activeFleet = vendorDevices.Where(d => 
            (string.Equals(d.StockStatus, "Used", StringComparison.OrdinalIgnoreCase) || string.Equals(d.StockStatus, "Activated", StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(d.VehicleNo) && 
            !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase)).ToList();

        var unpaidCount = activeFleet.Count(device =>
            !device.ExcludeFromUnpaid &&
            !string.Equals(device.Paid, "Yes", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(device.Paid, "No", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(device.InvoiceId) || !device.InvoiceId.StartsWith("old", StringComparison.OrdinalIgnoreCase)) &&
            !vendorTransactions.Any(t => string.Equals(t.VehicleNo, device.VehicleNo, StringComparison.OrdinalIgnoreCase)));

        var totalAmountPaid = vendorTransactions.Sum(t => t.Amount ?? 0m);
        var averageAmountPaid = vendorTransactions.Count > 0 ? totalAmountPaid / vendorTransactions.Count : 0m;

        var deviceTypeBreakdown = vendorDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.DeviceName))
            .GroupBy(d => {
                var name = d.DeviceName.Trim();
                if (string.Equals(name, "Wetrack140", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "WeTrack", StringComparison.OrdinalIgnoreCase))
                {
                    return "WeTrack";
                }
                return name;
            }, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count());

        var expiringDevices = vendorDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo)
                        && !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase)
                        && !d.IsNotInUse
                        && (DateValue.DaysFromNow(d.LatestExpiryDate) is >= 0 and <= 7
                            || (DateValue.DaysFromNow(d.LatestExpiryDate) < 0 && (d.DaysPastExpiry ?? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value) <= 30)
                            || string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => DateValue.DaysFromNow(d.LatestExpiryDate) ?? 999)
            .ToList();

        var viewModel = new VendorProfileViewModel
        {
            Vendor = vendor,
            TotalDevices = vendorDevices.Count,
            TotalAmountPaid = totalAmountPaid,
            AverageAmountPaid = averageAmountPaid,
            UnpaidDevicesCount = unpaidCount,
            DeviceTypes = deviceTypeBreakdown,
            ExpiringDevices = expiringDevices
        };

        return View(viewModel);
    }

    [HttpGet]
    [RequireModulePermission("Vendors", PermissionLevel.View)]
    public async Task<IActionResult> ExportExpiring(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();

        var vendor = await _context.Vendors.FirstOrDefaultAsync(v => v.Id == id);
        if (vendor == null)
        {
            // Fallback: search by vendor name if id was passed as vendor name
            vendor = await _context.Vendors.FirstOrDefaultAsync(v => v.Name.ToLower() == id.ToLower());
        }
        if (vendor == null) return NotFound();

        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        var allDevices = await _context.Devices
            .Where(d => d.Branch == branch && d.Vendor != null && d.Vendor != "")
            .ToListAsync();

        var vendorDevices = allDevices
            .Where(d => d.Vendor.Trim().Equals(vendor.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var expiringDevices = vendorDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo)
                        && !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase)
                        && !d.IsNotInUse
                        && (DateValue.DaysFromNow(d.LatestExpiryDate) is >= 0 and <= 7
                            || (DateValue.DaysFromNow(d.LatestExpiryDate) < 0 && (d.DaysPastExpiry ?? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value) <= 30)
                            || string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => DateValue.DaysFromNow(d.LatestExpiryDate) ?? 999)
            .ToList();

        var bytes = _spreadsheets.ExportVendorExpiringVehicles(vendor.Name, expiringDevices);
        var cleanVendorName = System.Text.RegularExpressions.Regex.Replace(vendor.Name ?? "Vendor", @"[^a-zA-Z0-9_\-]", "_");
        var fileName = $"Expiring_Vehicles_{cleanVendorName}_{DateTime.Now:yyyyMMdd}.xlsx";

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
