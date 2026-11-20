using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Data;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Areas.DMG.Controllers;
using Microsoft.AspNetCore.Mvc;

[Area("DMG")]
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
                    NetworkOperator = invoice?.NetworkOperator ?? g.FirstOrDefault(d => !string.IsNullOrEmpty(d.NetworkOperator))?.NetworkOperator ?? "",
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
    [RequireModulePermission("Inventory", PermissionLevel.Edit, AlternativeModule = "Devices")]
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
        var devices = await _db.Devices.IgnoreQueryFilters().Where(d => deviceIds.Contains(d.Id)).ToListAsync();
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
                device.StockStatus = targetStock;
                device.LifecycleStatus = !string.IsNullOrWhiteSpace(device.VehicleNo) ? "Active" : "Inventory";
                device.Status = device.LifecycleStatus;
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
            else if (newStatus.Equals("Dead", StringComparison.OrdinalIgnoreCase))
            {
                device.StockStatus = "Dead";
                device.LifecycleStatus = "Dead";
                device.Status = "Dead";
                updatedCount++;
            }
            else if (newStatus.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                device.StockStatus = "Error";
                device.LifecycleStatus = "Error";
                device.Status = "Error";
                updatedCount++;
            }
            else if (newStatus.Equals("New", StringComparison.OrdinalIgnoreCase))
            {
                device.StockStatus = "New";
                device.LifecycleStatus = "Inventory";
                device.Status = "Inventory";
                device.VehicleNo = "";
                device.ActivationDate = "";
                device.LatestExpiryDate = "";
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await _db.SaveChangesAsync();
            _store.InvalidateCache();
            TempData["Success"] = $"{updatedCount} device status{(updatedCount > 1 ? "es" : "")} updated successfully.";
        }
        else
        {
            TempData["Success"] = "Device statuses saved. No changes detected.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Inventory", PermissionLevel.Edit, AlternativeModule = "Devices")]
    public async Task<IActionResult> UpdateStatus(string id, string newStatus)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(newStatus))
        {
            TempData["Error"] = "Invalid request.";
            return RedirectToAction(nameof(Index));
        }

        var device = await _db.Devices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == id);
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
            device.VehicleNo = "";
            device.ActivationDate = "";
            device.LatestExpiryDate = "";
        }

        await _db.SaveChangesAsync();
        _store.InvalidateCache();

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

        try
        {
            await _store.UpdateAsync(data =>
            {
                var device = data.Devices.FirstOrDefault(d => d.Id == id);
                if (device != null)
                {
                    data.Devices.Remove(device);
                }
            });
            TempData["Success"] = "Device deleted successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error deleting device: {ex.Message}";
        }

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

        const string activeBranch = "DMG";
        var cleanInv = invoiceNo.Trim();
        bool isUnknown = string.Equals(cleanInv, "Unknown Batch", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(cleanInv, "Unknown", StringComparison.OrdinalIgnoreCase);

        try
        {
            int deletedDevices = 0;
            int unlinkedDevices = 0;

            await _store.UpdateAsync(data =>
            {
                var matchingDevices = data.Devices
                    .Where(d => (string.Equals(d.Branch, activeBranch, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch)) &&
                                (isUnknown
                                    ? (string.IsNullOrWhiteSpace(d.InvoiceId) || string.Equals(d.InvoiceId.Trim(), "Unknown Batch", StringComparison.OrdinalIgnoreCase) || string.Equals(d.InvoiceId.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase))
                                    : string.Equals((d.InvoiceId ?? "").Trim(), cleanInv, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (var d in matchingDevices)
                {
                    bool isUsed = !string.IsNullOrWhiteSpace(d.VehicleNo) ||
                                  (!string.IsNullOrWhiteSpace(d.StockStatus) && !string.Equals(d.StockStatus, "New", StringComparison.OrdinalIgnoreCase));
                    if (isUsed)
                    {
                        d.InvoiceId = "";
                        d.Notes = string.IsNullOrWhiteSpace(d.Notes) ? "(Invoice/Batch deleted)" : $"{d.Notes} (Invoice/Batch deleted)";
                        unlinkedDevices++;
                    }
                    else
                    {
                        data.Devices.Remove(d);
                        deletedDevices++;
                    }
                }

                if (!isUnknown)
                {
                    var invoicesToRemove = data.Invoices
                        .Where(i => (string.Equals(i.Branch, activeBranch, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(i.Branch)) &&
                                    string.Equals((i.InvoiceNo ?? "").Trim(), cleanInv, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var inv in invoicesToRemove)
                    {
                        data.Invoices.Remove(inv);
                    }
                }
            });

            TempData["Success"] = $"Batch '{invoiceNo}' deleted successfully ({deletedDevices} unassigned devices removed{(unlinkedDevices > 0 ? $", {unlinkedDevices} deployed devices unlinked" : "")}).";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error deleting batch: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [HttpPost]
    [RequireModulePermission("Inventory", PermissionLevel.View)]
    public async Task<IActionResult> ExportBatch(string invoiceNo, string? format = null, [FromQuery] List<string>? selectedIds = null, [FromForm] string? selectedIdsJson = null)
    {
        const string activeBranch = "DMG";
        var data = await _store.SnapshotAsync();

        List<DeviceRecord> batchDevices;
        if (string.Equals(invoiceNo, "Selected", StringComparison.OrdinalIgnoreCase) || string.Equals(invoiceNo, "All", StringComparison.OrdinalIgnoreCase))
        {
            batchDevices = data.Devices
                .Where(d => string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch))
                .OrderBy(d => d.SlNoSortable)
                .ToList();
        }
        else
        {
            batchDevices = data.Devices
                .Where(d => (string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch)) &&
                            (string.Equals(d.InvoiceId, invoiceNo, StringComparison.OrdinalIgnoreCase) ||
                            (BatchColorHelper.RtoBatchToInvoiceMap.TryGetValue(invoiceNo, out var invNo) && string.Equals(d.InvoiceId, invNo, StringComparison.OrdinalIgnoreCase)) ||
                            (BatchColorHelper.RtoInvoiceToBatchMap.TryGetValue(invoiceNo, out var bName) && string.Equals(d.InvoiceId, bName, StringComparison.OrdinalIgnoreCase))))
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

        bool isTaisys = string.Equals(format, "taisys", StringComparison.OrdinalIgnoreCase);

        // Activation file (Taisys commercial bulk format) is strictly for DMG branch only
        if (isTaisys || !string.Equals(format, "legacy", StringComparison.OrdinalIgnoreCase))
        {
            if (ids.Count == 0 && string.Equals(invoiceNo, "Selected", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Please select at least one device checkbox before downloading.";
                return RedirectToAction(nameof(Index));
            }
            var bytes = _spreadsheets.ExportTaisysBulkFormat(batchDevices);
            var safeInv = string.IsNullOrWhiteSpace(invoiceNo) ? "selected" : invoiceNo;
            var fileName = string.Equals(safeInv, "selected", StringComparison.OrdinalIgnoreCase)
                ? "commerical-taisysbulkformat.xlsx"
                : $"commerical-taisysbulkformat-{safeInv}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
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

    [HttpGet]
    [RequireModulePermission("Inventory", PermissionLevel.View)]
    public async Task<IActionResult> ExportAllBatches(string? format = null)
    {
        const string activeBranch = "DMG";
        var data = await _store.SnapshotAsync();

        if (string.Equals(format, "taisys", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Please select at least one device checkbox before downloading Taisys Bulk format.";
            return RedirectToAction(nameof(Index));
        }

        var inventoryDevices = data.Devices
            .Where(d => (string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch)) && !string.IsNullOrWhiteSpace(d.InvoiceId))
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
