using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Data;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Controllers;

[Authorize]
[RequireModulePermission("Inventory", PermissionLevel.View)]
public sealed class InventoryController : Controller
{
    private readonly IAppDataStore _store;
    private readonly ApplicationDbContext _db;
    private readonly SpreadsheetService _spreadsheets;

    public InventoryController(IAppDataStore store, ApplicationDbContext db, SpreadsheetService spreadsheets)
    {
        _store = store;
        _db = db;
        _spreadsheets = spreadsheets;
    }

    [HttpGet]
    public async Task<IActionResult> Index(InventoryViewModel filters)
    {
        var data = await _store.SnapshotAsync();

        var inventoryDevices = data.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.InvoiceId))
            .ToList();

        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var terms = filters.Query.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries);
            inventoryDevices = inventoryDevices.Where(d =>
            {
                var combined = string.Join(" ",
                    d.VehicleNo, d.DeviceName, d.Imei, d.Iccid, d.DeviceSerialNo,
                    d.SimAirtel, d.SimBsnl, d.Vendor, d.Owner,
                    d.InvoiceId, d.EngineNo, d.ChasisNo, d.CellNo,
                    d.Notes, d.PreviousImei, d.HardwareHistory,
                    d.Type, d.LifecycleStatus, d.Status, d.StockStatus,
                    DateValue.Short(d.LatestExpiryDate), DateValue.Short(d.ActivationDate), DateValue.Short(d.CreatedAt));
                return terms.All(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }


        var batches = inventoryDevices
            .GroupBy(d => string.IsNullOrWhiteSpace(d.InvoiceId) ? "Unknown" : d.InvoiceId)
            .Select(g =>
            {
                var invoice = data.Invoices.FirstOrDefault(i => 
                    string.Equals(i.InvoiceNo, g.Key, StringComparison.OrdinalIgnoreCase) ||
                    (BatchColorHelper.RtoBatchToInvoiceMap.TryGetValue(g.Key, out var invNo) && string.Equals(i.InvoiceNo, invNo, StringComparison.OrdinalIgnoreCase)) ||
                    (BatchColorHelper.RtoInvoiceToBatchMap.TryGetValue(g.Key, out var bName) && string.Equals(i.InvoiceNo, bName, StringComparison.OrdinalIgnoreCase)));

                IEnumerable<DeviceRecord> deviceList = g.OrderBy(d => d.SlNoSortable);

                return new InvoiceGroup
                {
                    InvoiceNo = g.Key,
                    InvoiceId = invoice?.Id ?? "",
                    DeviceType = invoice?.DeviceType ?? g.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceName))?.DeviceName ?? "",
                    Date = invoice?.Date ?? "",
                    Devices = deviceList.ToList()
                };
            })
            .OrderByDescending(b => b.Devices.Select(d => d.SlNoSortable).DefaultIfEmpty("").Max())
            .ThenByDescending(b => !string.IsNullOrEmpty(b.Date) ? b.Date : "0000-00-00")
            .ToList();

        var totalItems = batches.Count;
        var pagedBatches = (filters.PageSize >= 1000000 || filters.PageSize <= 0)
            ? batches
            : batches.Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize).ToList();

        // [Title: Batch Color Mapping - Assigns each distinct batch in the branch a unique color]
        ViewBag.BatchColorMap = BatchColorHelper.BuildBatchColorMap(data.Devices.Select(d => d.InvoiceId).Concat(data.Invoices.Select(i => i.InvoiceNo)));

        return View(new InventoryViewModel
        {
            Query = filters.Query,
            Batches = pagedBatches,
            SortColumn = filters.SortColumn,
            SortDirection = filters.SortDirection,
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalItems = totalItems
        });
    }

    [HttpPost]
    [RequireModulePermission("Inventory", PermissionLevel.Edit)]
    public async Task<IActionResult> BulkUpdateStatus(Dictionary<string, string>? statuses)
    {
        statuses ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Fallback: If model binding didn't populate statuses, parse directly from Request.Form
        if (statuses.Count == 0 && Request.HasFormContentType)
        {
            foreach (var key in Request.Form.Keys)
            {
                if (key.StartsWith("statuses[", StringComparison.OrdinalIgnoreCase) && key.EndsWith("]"))
                {
                    var id = key.Substring(9, key.Length - 10);
                    var val = Request.Form[key].ToString();
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(val))
                    {
                        statuses[id] = val;
                    }
                }
                else if (key.StartsWith("[", StringComparison.OrdinalIgnoreCase) && key.EndsWith("]"))
                {
                    var id = key.Substring(1, key.Length - 2);
                    var val = Request.Form[key].ToString();
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(val))
                    {
                        statuses[id] = val;
                    }
                }
            }
        }

        if (statuses.Count == 0)
        {
            TempData["Error"] = "No status changes provided.";
            return RedirectToAction(nameof(Index));
        }

        var deviceIds = statuses.Keys.ToList();
        var devices = await _db.Devices.Where(d => deviceIds.Contains(d.Id)).ToListAsync();
        if (devices.Count == 0)
        {
            TempData["Success"] = "Device statuses saved. No changes detected.";
            return RedirectToAction(nameof(Index));
        }

        var config = await _db.Config.AsNoTracking().ToDictionaryAsync(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        int months = int.TryParse(config.GetValueOrDefault("ACTIVATION_EXPIRY_MONTHS", "11"), out var mVal) ? mVal : 11;
        int days = int.TryParse(config.GetValueOrDefault("ACTIVATION_EXPIRY_DAYS", "15"), out var dVal) ? dVal : 15;
        var today = DateTime.UtcNow.Date;
        int updatedCount = 0;

        foreach (var device in devices)
        {
            if (!statuses.TryGetValue(device.Id, out var newStatus) || string.IsNullOrWhiteSpace(newStatus))
                continue;

            if (newStatus.Equals("Activated", StringComparison.OrdinalIgnoreCase) || newStatus.Equals("Used", StringComparison.OrdinalIgnoreCase))
            {
                var targetStock = !string.IsNullOrWhiteSpace(device.VehicleNo) ? "Used" : "Activated";
                if (!string.Equals(device.StockStatus, targetStock, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(device.ActivationDate))
                {
                    device.StockStatus = targetStock;
                    device.LifecycleStatus = "Active";
                    device.Status = "Active";
                    if (string.IsNullOrWhiteSpace(device.ActivationDate))
                    {
                        device.ActivationDate = today.ToString("yyyy-MM-dd");
                    }
                    if (string.IsNullOrWhiteSpace(device.LatestExpiryDate))
                    {
                        device.LatestExpiryDate = DeviceLifecycleHelper.CalculateExpiry(device, config) ?? today.AddMonths(months).AddDays(days).ToString("yyyy-MM-dd");
                    }
                    updatedCount++;
                }
            }
            else if (newStatus.Equals("Dead", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(device.StockStatus, "Dead", StringComparison.OrdinalIgnoreCase))
                {
                    device.StockStatus = "Dead";
                    device.LifecycleStatus = "Dead";
                    device.Status = "Dead";
                    updatedCount++;
                }
            }
            else if (newStatus.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(device.StockStatus, "Error", StringComparison.OrdinalIgnoreCase))
                {
                    device.StockStatus = "Error";
                    device.LifecycleStatus = "Error";
                    device.Status = "Error";
                    updatedCount++;
                }
            }
            else if (newStatus.Equals("New", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(device.StockStatus, "New", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(device.StockStatus, "Unused", StringComparison.OrdinalIgnoreCase))
                {
                    device.StockStatus = "New";
                    device.LifecycleStatus = "Inventory";
                    device.Status = "Inventory";
                    device.ActivationDate = "";
                    device.LatestExpiryDate = "";
                    updatedCount++;
                }
            }
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync();
            TempData["Success"] = $"{updatedCount} device status{(updatedCount > 1 ? "es" : "")} updated successfully.";
        }
        else
        {
            TempData["Success"] = "Device statuses saved. No changes detected.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Inventory", PermissionLevel.Edit)]
    public async Task<IActionResult> UpdateStatus(string id, string newStatus)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(newStatus))
        {
            TempData["Error"] = "Invalid request.";
            return RedirectToAction(nameof(Index));
        }

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (device == null)
        {
            TempData["Error"] = "Device not found.";
            return RedirectToAction(nameof(Index));
        }

        var config = await _db.Config.AsNoTracking().ToDictionaryAsync(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        int months = int.TryParse(config.GetValueOrDefault("ACTIVATION_EXPIRY_MONTHS", "11"), out var mVal) ? mVal : 11;
        int days = int.TryParse(config.GetValueOrDefault("ACTIVATION_EXPIRY_DAYS", "15"), out var dVal) ? dVal : 15;
        var today = DateTime.UtcNow.Date;

        if (newStatus.Equals("Activated", StringComparison.OrdinalIgnoreCase) || newStatus.Equals("Used", StringComparison.OrdinalIgnoreCase))
        {
            device.StockStatus = "Used";
            device.LifecycleStatus = "Active";
            device.Status = "Active";
            device.ActivationDate = today.ToString("yyyy-MM-dd");
            device.LatestExpiryDate = DeviceLifecycleHelper.CalculateExpiry(device, config) ?? today.AddMonths(months).AddDays(days).ToString("yyyy-MM-dd");
        }
        else if (newStatus.Equals("Dead", StringComparison.OrdinalIgnoreCase))
        {
            device.StockStatus = "Dead";
            device.LifecycleStatus = "Dead";
            device.Status = "Dead";
        }
        else if (newStatus.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            device.StockStatus = "Error";
            device.LifecycleStatus = "Error";
            device.Status = "Error";
        }
        else if (newStatus.Equals("New", StringComparison.OrdinalIgnoreCase))
        {
            device.StockStatus = "New";
            device.LifecycleStatus = "Inventory";
            device.Status = "Inventory";
            device.ActivationDate = "";
            device.LatestExpiryDate = "";
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = "Device state updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Inventory", PermissionLevel.Edit)]
    public async Task<IActionResult> DeleteDevice(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["Error"] = "Invalid request.";
            return RedirectToAction(nameof(Index));
        }

        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (device != null)
        {
            _db.Devices.Remove(device);
            await _db.SaveChangesAsync();
        }

        TempData["Success"] = "Device deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Inventory", PermissionLevel.Edit)]
    public async Task<IActionResult> DeleteBatch(string invoiceNo)
    {
        if (string.IsNullOrWhiteSpace(invoiceNo))
        {
            TempData["Error"] = "Invalid request.";
            return RedirectToAction(nameof(Index));
        }

        var devicesToDelete = await _db.Devices
            .Where(d => (string.IsNullOrWhiteSpace(d.InvoiceId) ? "Unknown Batch" : d.InvoiceId).Equals(invoiceNo, StringComparison.OrdinalIgnoreCase))
            .ToListAsync();

        if (devicesToDelete.Count > 0)
        {
            _db.Devices.RemoveRange(devicesToDelete);
        }

        var invoicesToDelete = await _db.Invoices
            .Where(i => i.InvoiceNo.Equals(invoiceNo, StringComparison.OrdinalIgnoreCase))
            .ToListAsync();

        if (invoicesToDelete.Count > 0)
        {
            _db.Invoices.RemoveRange(invoicesToDelete);
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = "Batch and associated invoice deleted successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("~/Inventory/ExportBatch")]
    [HttpPost("~/Inventory/ExportBatch")]
    [RequireModulePermission("Inventory", PermissionLevel.View)]
    public async Task<IActionResult> ExportBatch(string invoiceNo, string? format = null, [FromQuery] List<string>? selectedIds = null, [FromForm] string? selectedIdsJson = null)
    {
        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var data = await _store.SnapshotAsync();

        List<DeviceRecord> batchDevices;
        if (string.Equals(invoiceNo, "Selected", StringComparison.OrdinalIgnoreCase) || string.Equals(invoiceNo, "All", StringComparison.OrdinalIgnoreCase))
        {
            batchDevices = data.Devices.OrderBy(d => d.SlNoSortable).ToList();
        }
        else
        {
            batchDevices = data.Devices
                .Where(d => string.Equals(d.InvoiceId, invoiceNo, StringComparison.OrdinalIgnoreCase) ||
                            (BatchColorHelper.RtoBatchToInvoiceMap.TryGetValue(invoiceNo, out var invNo) && string.Equals(d.InvoiceId, invNo, StringComparison.OrdinalIgnoreCase)) ||
                            (BatchColorHelper.RtoInvoiceToBatchMap.TryGetValue(invoiceNo, out var bName) && string.Equals(d.InvoiceId, bName, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d.SlNoSortable)
                .ToList();
        }

        var ids = selectedIds ?? new List<string>();
        if (!string.IsNullOrWhiteSpace(selectedIdsJson))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(selectedIdsJson);
                if (parsed != null && parsed.Count > 0) ids = parsed;
            }
            catch { }
        }

        if (ids.Count > 0)
        {
            var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            batchDevices = batchDevices.Where(d => idSet.Contains(d.Id)).ToList();
        }

        if (string.Equals(format, "taisys", StringComparison.OrdinalIgnoreCase))
        {
            if (ids.Count == 0)
            {
                TempData["Error"] = "Please select at least one device checkbox before downloading Taisys Bulk format.";
                return RedirectToAction(nameof(Index));
            }
            var bytes = _spreadsheets.ExportTaisysBulkFormat(batchDevices);
            var safeInv = string.IsNullOrWhiteSpace(invoiceNo) ? "selected" : invoiceNo;
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"commerical-taisysbulkformat-{safeInv}.xlsx");
        }
        else
        {
            var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, invoiceNo, StringComparison.OrdinalIgnoreCase));
            var group = new InvoiceGroup
            {
                InvoiceNo = invoiceNo,
                InvoiceId = invoice?.Id ?? "",
                DeviceType = invoice?.DeviceType ?? "",
                Date = invoice?.Date ?? "",
                Devices = batchDevices
            };
            var bytes = _spreadsheets.ExportInventoryBatch(group, null, activeBranch);
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"mygts-inventory-batch-{invoiceNo}.xlsx");
        }
    }

    [HttpGet("~/Inventory/ExportAllBatches")]
    [RequireModulePermission("Inventory", PermissionLevel.View)]
    public async Task<IActionResult> ExportAllBatches(string? format = null)
    {
        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var data = await _store.SnapshotAsync();

        if (string.Equals(format, "taisys", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Please select at least one device checkbox before downloading Taisys Bulk format.";
            return RedirectToAction(nameof(Index));
        }

        var inventoryDevices = data.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.InvoiceId))
            .ToList();

        var batches = inventoryDevices
            .GroupBy(d => string.IsNullOrWhiteSpace(d.InvoiceId) ? "Unknown" : d.InvoiceId)
            .Select(g =>
            {
                var invoice = data.Invoices.FirstOrDefault(i => 
                    string.Equals(i.InvoiceNo, g.Key, StringComparison.OrdinalIgnoreCase) ||
                    (BatchColorHelper.RtoBatchToInvoiceMap.TryGetValue(g.Key, out var invNo) && string.Equals(i.InvoiceNo, invNo, StringComparison.OrdinalIgnoreCase)) ||
                    (BatchColorHelper.RtoInvoiceToBatchMap.TryGetValue(g.Key, out var bName) && string.Equals(i.InvoiceNo, bName, StringComparison.OrdinalIgnoreCase)));

                return new InvoiceGroup
                {
                    InvoiceNo = g.Key,
                    InvoiceId = invoice?.Id ?? "",
                    DeviceType = invoice?.DeviceType ?? g.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceName))?.DeviceName ?? "",
                    Date = invoice?.Date ?? "",
                    Devices = g.OrderBy(d => d.SlNoSortable).ToList()
                };
            })
            .OrderByDescending(b => b.Devices.Select(d => d.SlNoSortable).DefaultIfEmpty("").Max())
            .ThenByDescending(b => !string.IsNullOrEmpty(b.Date) ? b.Date : "0000-00-00")
            .ToList();

        var bytes = _spreadsheets.ExportInventoryAllBatches(batches, activeBranch);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "mygts-inventory-all-batches.xlsx");
    }
}
