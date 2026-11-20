using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;
using System.Security.Claims;

namespace MygtsMvc.Controllers;

[Authorize]
[RequireModulePermission("Devices", PermissionLevel.View)]
public sealed class DevicesController : Controller
{
    private readonly IAppDataStore _store;
    private readonly SpreadsheetService _spreadsheets;

    public DevicesController(IAppDataStore store, SpreadsheetService spreadsheets)
    {
        _store = store;
        _spreadsheets = spreadsheets;
    }

    private IEnumerable<DeviceRecord> FilterAndSortDevices(AppData data, DeviceFilterViewModel filters, string activeBranch)
    {
        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        IEnumerable<DeviceRecord> devices = data.Devices;

        // Synchronize lifecycle status and expiry dates dynamically
        foreach (var d in devices)
        {
            DeviceLifecycleHelper.SyncDevice(d, config);
        }

        if (!string.IsNullOrWhiteSpace(filters.SpecialFilter))
        {
            var today = DateTime.UtcNow.Date;
            devices = devices.Where(device =>
                (string.Equals(device.StockStatus, "Used", StringComparison.OrdinalIgnoreCase) || string.Equals(device.StockStatus, "Activated", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(device.VehicleNo) &&
                !string.Equals(device.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase));

            switch (filters.SpecialFilter)
            {
                case "Suspension Window":
                case "In Suspension":
                case "Suspension":
                    devices = devices.Where(device => string.Equals(device.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase));
                    break;
                case "Termination Window":
                case "In Termination":
                case "Terminated":
                    devices = devices.Where(device => string.Equals(device.LifecycleStatus, "Terminated", StringComparison.OrdinalIgnoreCase));
                    break;
                case "Dead":
                case "Dead / Deactivated":
                    devices = devices.Where(device => string.Equals(device.LifecycleStatus, "Dead", StringComparison.OrdinalIgnoreCase));
                    break;
                case "Suspensions Today":
                    devices = devices.Where(device => DateValue.Parse(device.LatestExpiryDate) == today && string.Equals(device.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase));
                    break;
                case "Terminating Tomorrow":
                    devices = devices.Where(device => DateValue.Parse(device.LatestExpiryDate) == today.AddDays(1));
                    break;
                case "Near-Dead":
                    devices = devices.Where(device => DateValue.DaysFromNow(device.LatestExpiryDate) is < 0 and >= -30);
                    break;
                case "Expired":
                    devices = devices.Where(device => DateValue.DaysFromNow(device.LatestExpiryDate) is < 0);
                    break;
                case "Expiring This Week":
                    devices = devices.Where(device => DateValue.DaysFromNow(device.LatestExpiryDate) is >= 0 and <= 7);
                    break;
                case "No Owner":
                    devices = devices.Where(device => string.IsNullOrWhiteSpace(device.Owner));
                    break;
                case "Unpaid Vehicles":
                case "Unpaid": // Keep backwards compatibility just in case
                    devices = devices.Where(device => !string.IsNullOrWhiteSpace(device.VehicleNo) 
                        && !device.ExcludeFromUnpaid 
                        && (string.IsNullOrWhiteSpace(device.InvoiceId) || !device.InvoiceId.StartsWith("old", StringComparison.OrdinalIgnoreCase))
                        && !data.Transactions.Any(t => string.Equals(t.VehicleNo, device.VehicleNo, StringComparison.OrdinalIgnoreCase)));
                    break;
            }
        }
        else
        {
            // If details are empty (no vehicle number / unallocated), exclude from devices page; they stay in Inventory
            // Unless user explicitly requests Inventory lifecycle or New stock filter
            bool explicitInventoryFilter = string.Equals(filters.Lifecycle, "Inventory", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(filters.Stock, "New", StringComparison.OrdinalIgnoreCase);

            if (!explicitInventoryFilter)
            {
                devices = devices.Where(device =>
                    !string.IsNullOrWhiteSpace(device.VehicleNo) &&
                    !string.Equals(device.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                devices = devices.Where(device => !string.Equals(device.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var terms = filters.Query.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries);
            devices = devices.Where(device =>
            {
                var combined = string.Join(" ",
                    device.SlNo, device.DeviceSerialNo, device.VehicleNo, device.DeviceName, device.Imei, device.Iccid,
                    device.SimAirtel, device.SimBsnl, device.Vendor, device.Owner,
                    device.InvoiceId, device.EngineNo, device.ChasisNo, device.CellNo,
                    device.VehicleMake, device.VehicleModel, device.Rto, device.Address,
                    device.ProofOfAddress, device.DocumentNumber,
                    device.Notes, device.PreviousImei, device.HardwareHistory,
                    device.Type, device.LifecycleStatus, device.Status, device.StockStatus,
                    DateValue.Short(device.LatestExpiryDate), DateValue.Short(device.ActivationDate), DateValue.Short(device.CreatedAt));
                return terms.All(term => combined.Contains(term, StringComparison.OrdinalIgnoreCase));
            });
        }

        if (!string.IsNullOrWhiteSpace(filters.Vendor)) devices = devices.Where(device => string.Equals(device.Vendor, filters.Vendor, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filters.Lifecycle)) devices = devices.Where(device => string.Equals(device.LifecycleStatus, filters.Lifecycle, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filters.Stock)) devices = devices.Where(device => string.Equals(device.StockStatus, filters.Stock, StringComparison.OrdinalIgnoreCase));

        // Order devices by Serial Number so both the serial numbers and the batches stay in continuous order, with latest batch on top
        devices = filters.SortColumn?.ToLowerInvariant() switch
        {
            "slno" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.SlNoSortable) : devices.OrderByDescending(d => d.SlNoSortable),
            "iccid" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.Iccid).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.Iccid).ThenByDescending(d => d.SlNoSortable),
            "imei" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.Imei).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.Imei).ThenByDescending(d => d.SlNoSortable),
            "vehicle" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.VehicleNo).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.VehicleNo).ThenByDescending(d => d.SlNoSortable),
            "status" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.LifecycleStatus).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.LifecycleStatus).ThenByDescending(d => d.SlNoSortable),
            "vendor" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.Vendor).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.Vendor).ThenByDescending(d => d.SlNoSortable),
            "type" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.Type).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.Type).ThenByDescending(d => d.SlNoSortable),
            "device" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.DeviceName).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.DeviceName).ThenByDescending(d => d.SlNoSortable),
            "activation" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.ActivationDate).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.ActivationDate).ThenByDescending(d => d.SlNoSortable),
            "engine" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.EngineNo).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.EngineNo).ThenByDescending(d => d.SlNoSortable),
            "chasis" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.ChasisNo).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.ChasisNo).ThenByDescending(d => d.SlNoSortable),
            "make" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.VehicleMake).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.VehicleMake).ThenByDescending(d => d.SlNoSortable),
            "model" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.VehicleModel).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.VehicleModel).ThenByDescending(d => d.SlNoSortable),
            "rto" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.Rto).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.Rto).ThenByDescending(d => d.SlNoSortable),
            "address" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.Address).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.Address).ThenByDescending(d => d.SlNoSortable),
            "docno" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.DocumentNumber).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.DocumentNumber).ThenByDescending(d => d.SlNoSortable),
            "cdacc" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.CdacCertificate).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.CdacCertificate).ThenByDescending(d => d.SlNoSortable),
            "vldtc" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.VldtCertificate).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.VldtCertificate).ThenByDescending(d => d.SlNoSortable),
            "deviceserial" or "serial" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.DeviceSerialNo).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.DeviceSerialNo).ThenByDescending(d => d.SlNoSortable),
            _ => devices.OrderByDescending(d => d.SlNoSortable)
        };

        return devices;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DeviceFilterViewModel filters)
    {
        var data = await _store.SnapshotAsync();
        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var sortedDevices = FilterAndSortDevices(data, filters, activeBranch);

        var sortedDevicesList = sortedDevices.ToList();
        var totalItems = sortedDevicesList.Count;
        var pagedDevices = (filters.PageSize >= 1000000 || filters.PageSize <= 0)
            ? sortedDevicesList
            : sortedDevicesList.Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize).ToList();

        // [Title: Batch Color Mapping - Assigns each distinct batch in the branch a unique color]
        ViewBag.BatchColorMap = BatchColorHelper.BuildBatchColorMap(data.Devices.Select(d => d.InvoiceId));

        return View(new DeviceFilterViewModel
        {
            Query = filters.Query,
            Vendor = filters.Vendor,
            Lifecycle = filters.Lifecycle,
            Stock = filters.Stock,
            SortColumn = filters.SortColumn,
            SortDirection = filters.SortDirection,
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalItems = totalItems,
            Devices = pagedDevices,
            Vendors = data.Devices.Select(device => device.Vendor).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
            LifecycleOptions = data.Devices.Select(device => device.LifecycleStatus).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
            StockOptions = data.Devices.Select(device => device.StockStatus).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList()
        });
    }

    [HttpGet]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public async Task<IActionResult> Create(string? returnUrl = null, bool forceBlank = false)
    {
        ViewBag.ReturnUrl = returnUrl;
        var data = await _store.SnapshotAsync();
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        if (!forceBlank)
        {
            var branchDevices = data.Devices
                .Where(d => string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch))
                .ToList();

            // 1. Check for unused pre-activated device in inventory
            var activatedDevice = branchDevices
                .Where(d => !d.IsMuted
                            && string.IsNullOrWhiteSpace(d.VehicleNo)
                            && !string.Equals(d.StockStatus, "Dead", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(d.StockStatus, "Error", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(d.StockStatus, "New", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(d.StockStatus, "Unused", StringComparison.OrdinalIgnoreCase)
                            && (string.Equals(d.StockStatus, "Activated", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(d.LifecycleStatus, "Activated", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(d.Status, "Activated", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d.SlNoSortable)
                .FirstOrDefault();

            if (activatedDevice != null)
            {
                var batchParam = !string.IsNullOrWhiteSpace(activatedDevice.InvoiceId)
                    ? $"expandBatch={Uri.EscapeDataString(activatedDevice.InvoiceId)}&"
                    : "";
                var inventoryReturnUrl = $"/{currentBranch}/Inventory?{batchParam}highlightDevice={activatedDevice.Id}";
                return RedirectToAction(nameof(Edit), new { id = activatedDevice.Id, returnUrl = inventoryReturnUrl });
            }

            // 2. If no activated devices, lead to the inventory batch which has unused devices with highlighting
            var nextUnusedDevice = branchDevices
                .Where(d => !d.IsMuted
                            && string.IsNullOrWhiteSpace(d.VehicleNo)
                            && !string.Equals(d.StockStatus, "Dead", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(d.StockStatus, "Error", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(d.InvoiceId)
                            && (string.Equals(d.StockStatus, "New", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(d.StockStatus, "Unused", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(d => d.SlNoSortable)
                .FirstOrDefault();

            if (nextUnusedDevice != null)
            {
                return Redirect($"/{currentBranch}/Inventory?expandBatch={Uri.EscapeDataString(nextUnusedDevice.InvoiceId)}&highlightDevice={nextUnusedDevice.Id}");
            }
        }
        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "11";
        ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "15";

        var activeDevices = data.Devices.Where(d => !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase)).ToList();
        ViewBag.ExistingDevices = activeDevices.OrderBy(d => d.VehicleNo).ToList();
        ViewBag.DevicesJson = System.Text.Json.JsonSerializer.Serialize(activeDevices);
        ViewBag.KnownVendors = data.Devices
            .Select(d => d.Vendor)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToList();

        return View(new DeviceFormViewModel());
    }

    [HttpPost]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public async Task<IActionResult> Create(DeviceFormViewModel model, string? returnUrl = null)
    {
        var dataSnapshot = await _store.SnapshotAsync();
        
        if (!string.IsNullOrWhiteSpace(model.SlNo) && dataSnapshot.Devices.Any(d => d.SlNo == model.SlNo))
        {
            ModelState.AddModelError("SlNo", "This serial number is already given to another device.");
        }

        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        if (activeBranch != "RTO" && model.ActivationDate.HasValue && !model.LatestExpiryDate.HasValue)
        {
            model.LatestExpiryDate = model.ActivationDate.Value.AddMonths(11).AddDays(15);
        }

        if (!ModelState.IsValid)
        {
            var config = dataSnapshot.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
            ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "11";
            ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "15";
            var activeDevices = dataSnapshot.Devices.Where(d => !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase)).ToList();
            ViewBag.ExistingDevices = activeDevices.OrderBy(d => d.VehicleNo).ToList();
            ViewBag.DevicesJson = System.Text.Json.JsonSerializer.Serialize(activeDevices);
            return View(model);
        }

        var device = ToRecord(model);
        await _store.UpdateAsync(data =>
        {
            if (string.IsNullOrWhiteSpace(device.SlNo) || device.SlNo == "0")
            {
                device.SlNo = (data.Devices.Count > 0 ? data.Devices.Max(d => int.TryParse(d.SlNo, out var val) ? val : 0) + 1 : 1).ToString();
            }

            if (device.InstallType == "Replacement" && device.ReplacementSource == "Existing Device" && !string.IsNullOrWhiteSpace(device.ReplacedDeviceId))
            {
                var oldDevice = data.Devices.FirstOrDefault(d => d.Id == device.ReplacedDeviceId);
                if (oldDevice != null)
                {
                    device.ReplacedDeviceSnapshot = System.Text.Json.JsonSerializer.Serialize(oldDevice);
                    oldDevice.LifecycleStatus = "Replaced";
                }
            }
            if (!string.IsNullOrWhiteSpace(device.InvoiceId))
            {
                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, device.InvoiceId, StringComparison.OrdinalIgnoreCase));
                if (invoice != null)
                {
                    device.UnitCost = invoice.CostPerDevice;
                    if (string.IsNullOrWhiteSpace(device.DeviceName) && !string.IsNullOrWhiteSpace(invoice.DeviceType))
                    {
                        device.DeviceName = invoice.DeviceType;
                    }
                }
            }
            data.Devices.Add(device);
        });
        TempData["Success"] = $"Device {device.VehicleNo} was added to the fleet.";
        var currentBranchForRed = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        if (!string.IsNullOrWhiteSpace(device.InvoiceId))
        {
            var batchUrl = $"/{currentBranchForRed}/Inventory?expandBatch={Uri.EscapeDataString(device.InvoiceId)}&highlightDevice={device.Id}";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                if (returnUrl.Contains("/Invoices", StringComparison.OrdinalIgnoreCase) ||
                    returnUrl.Contains("/Inventory", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(batchUrl);
                }
                return Redirect(returnUrl);
            }
            return Redirect(batchUrl);
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            if (returnUrl.Contains("/Invoices", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect($"/{currentBranchForRed}/Inventory");
            }
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(string id, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        var data = await _store.SnapshotAsync();
        var device = data.Devices.FirstOrDefault(item => item.Id == id);
        if (device is null) return NotFound();

        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "11";
        ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "15";

        var activeDevices = data.Devices.Where(d => !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase)).ToList();
        ViewBag.DevicesJson = System.Text.Json.JsonSerializer.Serialize(activeDevices);
        ViewBag.KnownVendors = data.Devices
            .Select(d => d.Vendor)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToList();

        return View(ToModel(device, config));
    }

    [HttpPost]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(DeviceFormViewModel model, string? returnUrl = null)
    {
        var dataSnapshot = await _store.SnapshotAsync();

        if (!string.IsNullOrWhiteSpace(model.SlNo) && dataSnapshot.Devices.Any(d => d.SlNo == model.SlNo && d.Id != model.Id))
        {
            ModelState.AddModelError("SlNo", "This serial number is already given to another device.");
        }

        var config = dataSnapshot.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

        // Auto-calculate expiry date if activation date is present but expiry date is missing
        if (model.ActivationDate.HasValue && !model.LatestExpiryDate.HasValue)
        {
            int months = int.TryParse(config.GetValueOrDefault("ACTIVATION_EXPIRY_MONTHS", "11"), out var mVal) ? mVal : 11;
            int days = int.TryParse(config.GetValueOrDefault("ACTIVATION_EXPIRY_DAYS", "15"), out var dVal) ? dVal : 15;
            model.LatestExpiryDate = model.ActivationDate.Value.AddMonths(months).AddDays(days);
        }

        if (!ModelState.IsValid)
        {
            ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "11";
            ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "15";
            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }
        var updated = false;
        await _store.UpdateAsync(data =>
        {
            var device = data.Devices.FirstOrDefault(item => item.Id == model.Id);
            if (device is null) return;
            Copy(model, device);
            if (!string.IsNullOrWhiteSpace(device.InvoiceId))
            {
                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, device.InvoiceId, StringComparison.OrdinalIgnoreCase));
                if (invoice != null) device.UnitCost = invoice.CostPerDevice;
            }
            updated = true;
        });
        if (!updated) return NotFound();
        TempData["Success"] = $"Device {model.VehicleNo} updated.";
        var currentBranchForRed = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        if (!string.IsNullOrWhiteSpace(model.InvoiceId))
        {
            var batchUrl = $"/{currentBranchForRed}/Inventory?expandBatch={Uri.EscapeDataString(model.InvoiceId)}&highlightDevice={model.Id}";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                if (returnUrl.Contains("/Invoices", StringComparison.OrdinalIgnoreCase) ||
                    returnUrl.Contains("/Inventory", StringComparison.OrdinalIgnoreCase))
                {
                    return Redirect(batchUrl);
                }
                return Redirect(returnUrl);
            }
            return Redirect(batchUrl);
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            if (returnUrl.Contains("/Invoices", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect($"/{currentBranchForRed}/Inventory");
            }
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public async Task<IActionResult> UpdateStatusFast(string id, string status)
    {
        await _store.UpdateDeviceAsync(id, device =>
        {
            if (status == "Added") { device.StockStatus = "New"; }
            else if (status == "Activated") { device.StockStatus = "Used"; device.LifecycleStatus = "Active"; }
            else if (status == "Dead") { device.StockStatus = "Dead"; device.LifecycleStatus = "Dead"; }
        });
        return Json(new { success = true });
    }

    [HttpPost]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = false;
        await _store.UpdateAsync(data =>
        {
            var device = data.Devices.FirstOrDefault(item => item.Id == id);
            if (device is null) return;
            data.Devices.Remove(device);
            deleted = true;
        });
        if (!deleted) return NotFound();
        TempData["Success"] = "Device removed from the fleet.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("~/Devices/ExportAll")]
    [HttpPost("~/Devices/ExportAll")]
    public async Task<IActionResult> ExportAll(
        [FromQuery] DeviceFilterViewModel filters, 
        [FromQuery] string? branch = null, 
        [FromQuery] bool thisPageOnly = false, 
        [FromQuery] string? format = null,
        [FromForm(Name = "format")] string? formFormat = null,
        [FromForm(Name = "selectedIds")] string? selectedIds = null,
        [FromQuery(Name = "selectedIds")] string? querySelectedIds = null)
    {
        var effFormat = !string.IsNullOrWhiteSpace(formFormat) ? formFormat : format;
        var effSelectedIds = !string.IsNullOrWhiteSpace(selectedIds) ? selectedIds : querySelectedIds;
        var data = await _store.SnapshotAsync();
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        var sortedDevices = FilterAndSortDevices(data, filters, activeBranch);

        string pageSuffix = "";
        if (!string.IsNullOrWhiteSpace(effSelectedIds))
        {
            var idList = effSelectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var idSet = new HashSet<string>(idList, StringComparer.OrdinalIgnoreCase);
            sortedDevices = sortedDevices.Where(d => idSet.Contains(d.Id) || (!string.IsNullOrWhiteSpace(d.SlNo) && idSet.Contains(d.SlNo))).ToList();
            pageSuffix = $"-selected-{sortedDevices.Count()}";
        }
        else if (thisPageOnly)
        {
            var page = filters.Page > 0 ? filters.Page : 1;
            var pageSize = filters.PageSize > 0 ? filters.PageSize : 50;
            if (pageSize < 1000000)
            {
                sortedDevices = sortedDevices.Skip((page - 1) * pageSize).Take(pageSize);
            }
            pageSuffix = $"-p{filters.Page}";
        }
        else
        {
            pageSuffix = "-filtered";
        }

        byte[] bytes;
        string filename;

        if (string.Equals(effFormat, "m2m", StringComparison.OrdinalIgnoreCase) || 
            string.Equals(effFormat, "bharat101", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effFormat, "kyc_upload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(effFormat, "upload_form", StringComparison.OrdinalIgnoreCase))
        {
            bytes = _spreadsheets.ExportDevicesM2m(sortedDevices);
            filename = $"kyc-upload-form{pageSuffix}-{DateTime.UtcNow:yyyyMMdd}.xls";
            return File(bytes, "application/vnd.ms-excel", filename);
        }
        else if (string.Equals(effFormat, "kyc_cl", StringComparison.OrdinalIgnoreCase) || 
                 string.Equals(effFormat, "kyc", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(effFormat, "commonlayer", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(effFormat, "common_layer", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(effFormat, "kyc_common_layer", StringComparison.OrdinalIgnoreCase))
        {
            bytes = _spreadsheets.ExportDevicesKycCl(sortedDevices);
            filename = $"kyc-common-layer{pageSuffix}-{DateTime.UtcNow:yyyyMMdd}.xls";
            return File(bytes, "application/vnd.ms-excel", filename);
        }
        else
        {
            bytes = _spreadsheets.ExportDevices(sortedDevices, activeBranch);
            filename = $"mygts-devices-{activeBranch.ToLowerInvariant()}{pageSuffix}-{DateTime.UtcNow:yyyyMMdd}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
        }
    }

    [HttpGet("~/Devices/Export")]
    public async Task<IActionResult> Export()
    {
        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var data = await _store.SnapshotAsync();
        var bytes = _spreadsheets.Export(data, activeBranch);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"mygts-export-{activeBranch.ToLower()}-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("~/Devices/DownloadSample")]
    [AllowAnonymous]
    public IActionResult DownloadSample([FromQuery] string? branch = null, [FromQuery] string? format = null)
    {
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";

        if (string.Equals(format, "m2m", StringComparison.OrdinalIgnoreCase) || 
            string.Equals(format, "bharat101", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "kyc_upload", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "upload_form", StringComparison.OrdinalIgnoreCase))
        {
            var sampleRows = new[]
            {
                new DeviceRecord
                {
                    SlNo = "1",
                    Iccid = "8991102506560086162",
                    Imei = "358250331575976",
                    EngineNo = "GCHZ409628",
                    ChasisNo = "MB1NACFD7GRCN9908",
                    VehicleNo = "KA35AA3698",
                    Owner = "KAMATH NAIK",
                    CellNo = "9916655979",
                    Address = "HOSAPETE, BELLARY KA 583201",
                    ProofOfAddress = "Aadhar",
                    DocumentNumber = "804290418601"
                }
            };
            return File(_spreadsheets.ExportDevicesM2m(sampleRows), "application/vnd.ms-excel", "sample-m2m-bharat101.xls");
        }
        else if (string.Equals(format, "kyc_cl", StringComparison.OrdinalIgnoreCase) || 
                 string.Equals(format, "kyc", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(format, "common_layer", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(format, "kyc_common_layer", StringComparison.OrdinalIgnoreCase))
        {
            var sampleRows = new[]
            {
                new DeviceRecord
                {
                    Imei = "358250331701564",
                    EngineNo = "GCHZ409628",
                    ChasisNo = "MB1NACFD7GRCN9908",
                    Type = "OLD",
                    VehicleMake = "ASHOK LEYLAND LTD",
                    VehicleModel = "ASHOK LEYLAND 3718IL",
                    Owner = "KAUSAR JAHAN",
                    CellNo = "7019710701",
                    Rto = "KA-09",
                    Address = "D NO-1227, 9TH WARD, HOSAPETE, BELLARY KA 583201",
                    ProofOfAddress = "AADHAR",
                    DocumentNumber = "268576399133",
                    VehicleNo = "KA34B4259"
                }
            };
            return File(_spreadsheets.ExportDevicesKycCl(sampleRows), "application/vnd.ms-excel", "sample-kyc-cl.xls");
        }

        string filename;
        string sheetName;
        string[] headers;
        List<string[]> rows;

        if (activeBranch == "RTO")
        {
            filename = "sample-devices-rto.xlsx";
            sheetName = "Devices_RTO";
            headers = ["sl_no", "vehicle_no", "iccid", "imei", "device_serial_no", "sim_airtel", "sim_bsnl", "status", "device_name", "type", "registration_date", "latest_expiry_date", "vendor", "owner", "cell_no", "activation_date", "engine_no", "chasis_no", "vehicle_make", "vehicle_model", "rto", "address", "proof_of_address", "document_number", "invoice_id", "notes", "days_past_expiry", "lifecycle_status", "stock_status"];
            rows =
            [
                ["1", "KA01AB5678", "899102506450567099", "358980102326353", "SN-RTO-1001", "5754219776805", "5752052584890", "Active", "Bharat101", "NEW", "2026-01-10", "2027-01-09", "Sample Vendor", "Sample Owner", "9876543210", "2026-01-15", "ENG12345", "CHS12345", "Tata", "Ace Gold", "KA01", "123 MG Road, Bangalore", "Aadhar", "1234-5678-9012", "INV-2026-RTO-01", "Sample RTO device notes", "0", "Active", "Used"],
                ["2", "KA01AB5679", "899102506450567222", "358980102354850", "SN-RTO-1002", "5754219777013", "5752052584903", "Active", "Bharat101", "NEW", "2026-01-10", "2027-01-09", "Sample Vendor", "Sample Owner", "9876543211", "2026-01-15", "ENG12346", "CHS12346", "Mahindra", "Bolero Maxi", "KA01", "456 Indiranagar, Bangalore", "PAN", "ABCDE1234F", "INV-2026-RTO-01", "Sample RTO device 2", "0", "Active", "Used"]
            ];
        }
        else
        {
            filename = "sample-devices-dmg.xlsx";
            sheetName = "Devices_DMG";
            headers = ["sl_no", "vehicle_no", "iccid", "imei", "sim_airtel", "sim_bsnl", "status", "type", "device_name", "invoice_id", "vendor", "owner", "cell_no", "activation_date", "latest_expiry_date", "engine_no", "chasis_no", "proof_of_address", "document_number", "notes", "days_past_expiry", "lifecycle_status", "stock_status"];
            rows =
            [
                ["1", "KA01AB1234", "899102506450567099", "358980102326353", "5754219776805", "5752052584890", "Active", "GPS", "Bharat101", "INV-DMG-2026-001", "Sample Vendor", "Sample Owner", "9876543210", "2026-01-15", "2027-01-14", "ENG12345", "CHS12345", "Aadhaar", "DOC-9876", "Sample DMG device notes", "0", "Active", "Used"],
                ["2", "KA01AB1235", "899102506450567222", "358980102354850", "5754219777013", "5752052584903", "Active", "GPS", "Bharat101", "INV-DMG-2026-001", "Sample Vendor", "Sample Owner", "9876543211", "2026-01-15", "2027-01-14", "ENG12346", "CHS12346", "VoterID", "DOC-9877", "Sample DMG device 2", "0", "Active", "Used"]
            ];
        }

        var bytes = _spreadsheets.CreateFormattedSampleWorkbook(sheetName, headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }

    [HttpGet]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public IActionResult Import()
    {
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        ViewBag.CurrentBranch = currentBranch;
        return View(new ImportViewModel());
    }

    [HttpPost]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public async Task<IActionResult> Import(ImportViewModel model)
    {
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        ViewBag.CurrentBranch = currentBranch;

        if (model.Workbook is null || model.Workbook.Length == 0)
        {
            ModelState.AddModelError(nameof(model.Workbook), "Choose an Excel (.xlsx) or CSV (.csv) file to import.");
            return View(model);
        }

        var ext = Path.GetExtension(model.Workbook.FileName);
        if (!string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) && !string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.Workbook), "Unsupported file format. Please upload an Excel (.xlsx) or CSV (.csv) file.");
            return View(model);
        }

        List<DeviceRecord> parsedDevices;
        try
        {
            await using var stream = model.Workbook.OpenReadStream();
            parsedDevices = _spreadsheets.ParseImportedDevices(stream, model.Workbook.FileName);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(model.Workbook), $"Error reading the file: {ex.Message}");
            return View(model);
        }

        if (parsedDevices.Count == 0)
        {
            ModelState.AddModelError(nameof(model.Workbook), "No valid device rows found in the file. Ensure the file contains headers including 'imei' and 'iccid'.");
            return View(model);
        }

        var data = await _store.SnapshotAsync();
        var existingDevices = data.Devices.Where(d => string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase)).ToList();

        var reviewItems = new List<DeviceImportReviewItemViewModel>();
        int newCount = 0;
        int conflictCount = 0;

        for (int i = 0; i < parsedDevices.Count; i++)
        {
            var imported = parsedDevices[i];
            imported.Branch = currentBranch;
            var (cleanVeh, prevDev) = DeviceLifecycleHelper.ParseVehicleSlash(imported.VehicleNo);
            imported.VehicleNo = cleanVeh;
            if (!string.IsNullOrWhiteSpace(prevDev))
            {
                imported.HardwareHistory = string.IsNullOrWhiteSpace(imported.HardwareHistory) 
                    ? $"Replaced from: {prevDev}" 
                    : $"{imported.HardwareHistory}; Replaced from: {prevDev}";
            }

            // Strict conflict check: both IMEI and ICCID must match an existing device
            var existingMatch = existingDevices.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.Imei) && !string.IsNullOrWhiteSpace(imported.Imei) &&
                !string.IsNullOrWhiteSpace(d.Iccid) && !string.IsNullOrWhiteSpace(imported.Iccid) &&
                string.Equals(d.Imei.Trim(), imported.Imei.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Iccid.Trim(), imported.Iccid.Trim(), StringComparison.OrdinalIgnoreCase)
            );

            bool isConflict = existingMatch != null;
            if (isConflict)
            {
                conflictCount++;
            }
            else
            {
                newCount++;
            }

            reviewItems.Add(new DeviceImportReviewItemViewModel
            {
                Index = i,
                Imported = imported,
                Existing = existingMatch,
                IsConflict = isConflict,
                Action = isConflict ? "replace" : "insert"
            });
        }

        var reviewModel = new DeviceImportReviewViewModel
        {
            Branch = currentBranch,
            TotalRows = parsedDevices.Count,
            NewCount = newCount,
            ConflictCount = conflictCount,
            Items = reviewItems,
            StagedDevicesJson = System.Text.Json.JsonSerializer.Serialize(parsedDevices),
            ExcludeFromUnpaid = model.ExcludeFromUnpaid
        };

        return View("ImportReview", reviewModel);
    }

    [HttpPost]
    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    [RequestFormLimits(ValueCountLimit = 100000, ValueLengthLimit = 50 * 1024 * 1024)]
    public async Task<IActionResult> ConfirmImport(DeviceImportConfirmSubmitModel model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.StagedDevicesJson))
        {
            TempData["Error"] = "No device rows were submitted for import. Please try uploading the file again.";
            return RedirectToAction(nameof(Import));
        }

        var currentBranch = !string.IsNullOrWhiteSpace(model.Branch) ? model.Branch : (Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG");

        List<DeviceRecord>? stagedDevices;
        try
        {
            stagedDevices = System.Text.Json.JsonSerializer.Deserialize<List<DeviceRecord>>(model.StagedDevicesJson);
        }
        catch
        {
            stagedDevices = null;
        }

        if (stagedDevices == null || stagedDevices.Count == 0)
        {
            TempData["Error"] = "No device rows found in staged data. Please try uploading the file again.";
            return RedirectToAction(nameof(Import));
        }

        int addedCount = 0;
        int replacedCount = 0;
        int skippedCount = 0;

        await _store.UpdateAsync(current =>
        {
            var maxSlNo = current.Devices.Count > 0 ? current.Devices.Max(d => int.TryParse(d.SlNo, out var val) ? val : 0) : 0;

            for (int i = 0; i < stagedDevices.Count; i++)
            {
                var staged = stagedDevices[i];
                var (cleanVeh, prevDev) = DeviceLifecycleHelper.ParseVehicleSlash(staged.VehicleNo);
                staged.VehicleNo = cleanVeh;
                if (!string.IsNullOrWhiteSpace(prevDev))
                {
                    staged.HardwareHistory = string.IsNullOrWhiteSpace(staged.HardwareHistory) 
                        ? $"Replaced from: {prevDev}" 
                        : $"{staged.HardwareHistory}; Replaced from: {prevDev}";
                }
                var action = model.Actions != null && model.Actions.TryGetValue(i, out var act) ? act : "insert";

                if (string.Equals(action, "keep", StringComparison.OrdinalIgnoreCase))
                {
                    skippedCount++;
                    continue;
                }

                if (string.Equals(action, "replace", StringComparison.OrdinalIgnoreCase))
                {
                    var existing = current.Devices.FirstOrDefault(d =>
                        !string.IsNullOrWhiteSpace(d.Imei) && !string.IsNullOrWhiteSpace(staged.Imei) &&
                        !string.IsNullOrWhiteSpace(d.Iccid) && !string.IsNullOrWhiteSpace(staged.Iccid) &&
                        string.Equals(d.Imei.Trim(), staged.Imei.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d.Iccid.Trim(), staged.Iccid.Trim(), StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        if (!string.IsNullOrWhiteSpace(staged.VehicleNo)) existing.VehicleNo = staged.VehicleNo.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.DeviceSerialNo)) existing.DeviceSerialNo = staged.DeviceSerialNo.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.SimAirtel)) existing.SimAirtel = staged.SimAirtel.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.SimBsnl)) existing.SimBsnl = staged.SimBsnl.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Status)) existing.Status = staged.Status.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Type)) existing.Type = SpreadsheetService.NormalizeType(staged.Type);
                        if (!string.IsNullOrWhiteSpace(staged.DeviceName)) existing.DeviceName = staged.DeviceName.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.InvoiceId)) existing.InvoiceId = staged.InvoiceId.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Vendor)) existing.Vendor = staged.Vendor.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.HardwareHistory)) existing.HardwareHistory = staged.HardwareHistory.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Owner)) existing.Owner = staged.Owner.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.CellNo)) existing.CellNo = staged.CellNo.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.RegistrationDate)) existing.RegistrationDate = staged.RegistrationDate.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.ActivationDate)) existing.ActivationDate = staged.ActivationDate.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.LatestExpiryDate)) existing.LatestExpiryDate = staged.LatestExpiryDate.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.EngineNo)) existing.EngineNo = staged.EngineNo.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.ChasisNo)) existing.ChasisNo = staged.ChasisNo.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.VehicleMake)) existing.VehicleMake = staged.VehicleMake.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.VehicleModel)) existing.VehicleModel = staged.VehicleModel.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Rto)) existing.Rto = staged.Rto.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Address)) existing.Address = staged.Address.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.ProofOfAddress)) existing.ProofOfAddress = staged.ProofOfAddress.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.DocumentNumber)) existing.DocumentNumber = staged.DocumentNumber.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Notes)) existing.Notes = staged.Notes.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.LifecycleStatus)) existing.LifecycleStatus = staged.LifecycleStatus.Trim();
                        existing.StockStatus = !string.IsNullOrWhiteSpace(staged.StockStatus) ? staged.StockStatus.Trim() : (!string.IsNullOrWhiteSpace(existing.VehicleNo) ? "Used" : "New");
                        if (model.ExcludeFromUnpaid || staged.InvoiceId?.StartsWith("old", StringComparison.OrdinalIgnoreCase) == true) existing.ExcludeFromUnpaid = true;

                        replacedCount++;
                        continue;
                    }
                }

                // Default: insert as new device
                staged.Id = Guid.NewGuid().ToString("N");
                staged.SlNo = (++maxSlNo).ToString();
                staged.Branch = currentBranch;
                staged.ExcludeFromUnpaid = model.ExcludeFromUnpaid || (staged.InvoiceId?.StartsWith("old", StringComparison.OrdinalIgnoreCase) == true);
                if (string.IsNullOrWhiteSpace(staged.StockStatus))
                {
                    staged.StockStatus = !string.IsNullOrWhiteSpace(staged.VehicleNo) ? "Used" : "New";
                }
                if (string.IsNullOrWhiteSpace(staged.Status)) staged.Status = "Active";
                if (string.IsNullOrWhiteSpace(staged.LifecycleStatus)) staged.LifecycleStatus = "Active";
                if (string.IsNullOrWhiteSpace(staged.CreatedAt)) staged.CreatedAt = DateTime.UtcNow.ToString("O");

                current.Devices.Add(staged);
                addedCount++;
            }
        });

        TempData["Success"] = $"Import completed for {currentBranch}: {addedCount} new devices added, {replacedCount} existing devices updated, {skippedCount} existing devices kept.";
        return RedirectToAction(nameof(Index));
    }

    private static DeviceRecord ToRecord(DeviceFormViewModel model)
    {
        var record = new DeviceRecord { Id = model.Id ?? Guid.NewGuid().ToString("N") };
        Copy(model, record);
        return record;
    }


    private static void Copy(DeviceFormViewModel model, DeviceRecord record)
    {
        var (cleanVeh, prevDev) = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo);
        record.SlNo = (model.SlNo ?? "").Trim(); record.DeviceSerialNo = (model.DeviceSerialNo ?? "").Trim(); record.VehicleNo = cleanVeh; record.DeviceName = (model.DeviceName ?? "").Trim(); record.InstallType = model.InstallType; record.Type = SpreadsheetService.NormalizeType(model.Type); record.Vendor = (model.Vendor ?? "").Trim(); record.Owner = (model.Owner ?? "").Trim(); record.CellNo = (model.CellNo ?? "").Trim(); record.RegistrationDate = model.RegistrationDate?.ToString("yyyy-MM-dd") ?? ""; record.Imei = (model.Imei ?? "").Trim(); record.Iccid = (model.Iccid ?? "").Trim(); record.SimAirtel = (model.SimAirtel ?? "").Trim(); record.SimBsnl = (model.SimBsnl ?? "").Trim(); record.ActivationDate = model.ActivationDate?.ToString("yyyy-MM-dd") ?? ""; record.LatestExpiryDate = model.LatestExpiryDate?.ToString("yyyy-MM-dd") ?? ""; record.LifecycleStatus = string.IsNullOrWhiteSpace(model.LifecycleStatus) ? "Active" : model.LifecycleStatus; record.Status = record.LifecycleStatus;
        if (!string.IsNullOrWhiteSpace(cleanVeh))
        {
            record.StockStatus = "Used";
        }
        else
        {
            if (string.Equals(model.StockStatus, "Used", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(model.StockStatus, "Activated", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(record.ActivationDate))
            {
                record.StockStatus = "Activated";
            }
            else
            {
                record.StockStatus = string.IsNullOrWhiteSpace(model.StockStatus) ? "New" : model.StockStatus;
            }
        }
        record.Notes = (model.Notes ?? "").Trim(); record.CreatedAt = string.IsNullOrWhiteSpace(record.CreatedAt) ? DateTime.UtcNow.ToString("O") : record.CreatedAt; record.EngineNo = (model.EngineNo ?? "").Trim(); record.ChasisNo = (model.ChasisNo ?? "").Trim(); record.VehicleMake = (model.VehicleMake ?? "").Trim(); record.VehicleModel = (model.VehicleModel ?? "").Trim(); record.Rto = SpreadsheetService.NormalizeRto(model.Rto, cleanVeh); record.Address = (model.Address ?? "").Trim(); record.ProofOfAddress = (model.ProofOfAddress ?? "").Trim(); record.DocumentNumber = (model.DocumentNumber ?? "").Trim(); record.PreviousImei = (model.PreviousImei ?? "").Trim(); 
        var hh = (model.HardwareHistory ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(prevDev)) { var prevTxt = $"Replaced from: {prevDev}"; hh = string.IsNullOrWhiteSpace(hh) ? prevTxt : $"{hh}; {prevTxt}"; }
        record.HardwareHistory = hh; record.InvoiceId = (model.InvoiceId ?? "").Trim(); record.ReplacementSource = model.ReplacementSource; record.ReplacedDeviceId = model.ReplacedDeviceId; record.ReplacedDeviceSnapshot = model.ReplacedDeviceSnapshot; record.ExcludeFromUnpaid = model.ExcludeFromUnpaid; record.NetworkOperator = (model.NetworkOperator ?? "").Trim();
        record.CdacCertificate = string.IsNullOrWhiteSpace(model.CdacCertificate) ? "NO" : model.CdacCertificate.Trim().ToUpper();
        record.VldtCertificate = string.IsNullOrWhiteSpace(model.VldtCertificate) ? "NO" : model.VldtCertificate.Trim().ToUpper();
    }

    private static DeviceFormViewModel ToModel(DeviceRecord device, Dictionary<string, string>? config = null)
    {
        var registrationDate = DateValue.Parse(device.RegistrationDate);
        var activationDate = DateValue.Parse(device.ActivationDate);
        var expiryDate = DateValue.Parse(device.LatestExpiryDate);

        if (activationDate.HasValue && !expiryDate.HasValue)
        {
            int months = 11;
            int days = 15;
            if (config != null)
            {
                if (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var mStr) && int.TryParse(mStr, out var mVal)) months = mVal;
                if (config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var dStr) && int.TryParse(dStr, out var dVal)) days = dVal;
            }
            expiryDate = activationDate.Value.AddMonths(months).AddDays(days);
        }

        return new()
        {
            Id = device.Id,
            SlNo = device.SlNo,
            DeviceSerialNo = device.DeviceSerialNo,
            VehicleNo = device.VehicleNo,
            DeviceName = device.DeviceName,
            InstallType = device.InstallType,
            Type = device.Type,
            Vendor = device.Vendor,
            Owner = device.Owner,
            CellNo = device.CellNo,
            RegistrationDate = registrationDate,
            Imei = device.Imei,
            Iccid = device.Iccid,
            SimAirtel = device.SimAirtel,
            SimBsnl = device.SimBsnl,
            ActivationDate = activationDate,
            LatestExpiryDate = expiryDate,
            LifecycleStatus = device.LifecycleStatus,
            StockStatus = device.StockStatus,
            Notes = device.Notes,
            EngineNo = device.EngineNo,
            ChasisNo = device.ChasisNo,
            VehicleMake = device.VehicleMake,
            VehicleModel = device.VehicleModel,
            Rto = device.Rto,
            Address = device.Address,
            ProofOfAddress = device.ProofOfAddress,
            DocumentNumber = device.DocumentNumber,
            PreviousImei = device.PreviousImei,
            HardwareHistory = device.HardwareHistory,
            InvoiceId = device.InvoiceId,
            ReplacementSource = device.ReplacementSource,
            ReplacedDeviceId = device.ReplacedDeviceId,
            ReplacedDeviceSnapshot = device.ReplacedDeviceSnapshot,
            ExcludeFromUnpaid = device.ExcludeFromUnpaid,
            NetworkOperator = device.NetworkOperator,
            CdacCertificate = string.IsNullOrWhiteSpace(device.CdacCertificate) ? "NO" : device.CdacCertificate,
            VldtCertificate = string.IsNullOrWhiteSpace(device.VldtCertificate) ? "NO" : device.VldtCertificate
        };
    }

    private static string CleanHiddenSpaces(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var cleaned = System.Text.RegularExpressions.Regex.Replace(input, @"[\u200B-\u200D\uFEFF\u200E\u200F\u202A-\u202E]", "");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[\u00A0\u1680\u2000-\u200A\u202F\u205F\u3000]", " ");
        return cleaned.Trim();
    }

    private static string CompactAlphaNumeric(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var cleaned = CleanHiddenSpaces(input);
        return System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^a-zA-Z0-9]", "").ToLowerInvariant();
    }

    // ===== DATA SHARE - Cross-branch global device search =====
    [HttpGet("~/Devices/DataShare")]
    [RequireModulePermission("DataShare", PermissionLevel.View)]
    public async Task<IActionResult> DataShare([FromQuery] string? query = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var data = await _store.SnapshotAsync();
        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        // Use all devices from the store snapshot (no branch filter on the store itself but we need ALL branches)
        // We'll do a direct DB query ignoring branch filters
        IEnumerable<DeviceRecord> allDevices;
        using (var scope = HttpContext.RequestServices.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            allDevices = ctx.Devices.IgnoreQueryFilters().ToList();
        }

        foreach (var d in allDevices)
        {
            DeviceLifecycleHelper.SyncDevice(d, config);
        }

        IEnumerable<DeviceRecord> results = allDevices;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var cleanedQuery = CleanHiddenSpaces(query);
            var rawTerms = cleanedQuery.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var terms = rawTerms.Select(CleanHiddenSpaces).Where(t => !string.IsNullOrEmpty(t)).ToList();

            results = results.Where(d =>
            {
                var combined = CleanHiddenSpaces(string.Join(" ",
                    d.VehicleNo, d.Iccid, d.Imei, d.SimAirtel, d.SimBsnl,
                    d.DeviceName, d.NetworkOperator, d.Vendor, d.Owner, d.CellNo,
                    d.EngineNo, d.ChasisNo,
                    DateValue.Short(d.LatestExpiryDate)));

                var compactCombined = CompactAlphaNumeric(string.Join(" ",
                    d.VehicleNo, d.Iccid, d.Imei, d.SimAirtel, d.SimBsnl,
                    d.DeviceName, d.NetworkOperator, d.Vendor, d.Owner, d.CellNo,
                    d.EngineNo, d.ChasisNo,
                    DateValue.Short(d.LatestExpiryDate)));

                return terms.All(t =>
                {
                    if (combined.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;

                    var compactT = CompactAlphaNumeric(t);
                    if (!string.IsNullOrEmpty(compactT) && compactCombined.Contains(compactT, StringComparison.OrdinalIgnoreCase)) return true;

                    var words = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 1 && words.All(w => combined.Contains(w, StringComparison.OrdinalIgnoreCase))) return true;

                    return false;
                });
            });
        }

        var list = results.OrderBy(d => d.VehicleNo).ThenBy(d => d.Branch).ToList();
        var totalItems = list.Count;
        var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        // Load most recent transaction per vehicle for "Previously Used" history
        Dictionary<string, TransactionRecord> lastTxMap = new();
        using (var scope = HttpContext.RequestServices.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            var pagedVehicles = paged
                .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo))
                .Select(d => d.VehicleNo.Replace(" ", "").ToUpperInvariant())
                .Distinct()
                .ToList();

            var txs = ctx.Transactions.IgnoreQueryFilters()
                .Where(t => !string.IsNullOrEmpty(t.VehicleNo))
                .OrderByDescending(t => t.Date)
                .ToList();

            lastTxMap = txs
                .GroupBy(t => t.VehicleNo.Replace(" ", "").ToUpperInvariant())
                .ToDictionary(g => g.Key, g => g.First());
        }
        ViewBag.LastTxMap = lastTxMap;

        return View(new DataShareViewModel
        {
            Query = query,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            Results = paged
        });
    }

    // ===== DATA SHARE HISTORY & LOGGING =====
    [HttpPost("~/Devices/LogDataShare")]
    [RequireModulePermission("DataShare", PermissionLevel.View)]
    public async Task<IActionResult> LogDataShare([FromBody] DataShareLogRequest request)
    {
        if (request == null) return BadRequest("Invalid request.");

        var record = new DataShareRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Branch = string.IsNullOrWhiteSpace(request.DeviceBranch) ? "DMG" : request.DeviceBranch,
            VehicleNo = request.VehicleNo?.Trim() ?? "",
            Imei = request.Imei?.Trim() ?? "",
            Iccid = request.Iccid?.Trim() ?? "",
            DeviceName = request.DeviceName?.Trim() ?? "",
            DeviceBranch = request.DeviceBranch?.Trim() ?? "",
            SharedBy = User.Identity?.Name ?? "User",
            SharedByEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "",
            SharedByRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "Team",
            SharedAt = DateTime.UtcNow.ToString("O"),
            Recipient = request.Recipient?.Trim() ?? "",
            Notes = request.Notes?.Trim() ?? ""
        };

        using (var scope = HttpContext.RequestServices.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            ctx.DataShareRecords.Add(record);
            await ctx.SaveChangesAsync();
        }

        return Json(new { success = true, id = record.Id, sharedAt = record.SharedAtFormatted, sharedBy = record.SharedBy });
    }

    [HttpGet("~/Devices/DataShareHistory")]
    [RequireModulePermission("DataShare", PermissionLevel.View)]
    public async Task<IActionResult> DataShareHistory([FromQuery] string? query = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        List<DataShareRecord> allRecords;
        using (var scope = HttpContext.RequestServices.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MygtsMvc.Data.ApplicationDbContext>();
            allRecords = await ctx.DataShareRecords.IgnoreQueryFilters()
                .OrderByDescending(r => r.SharedAt)
                .ToListAsync();
        }

        var todayPrefix = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var totalSharesToday = allRecords.Count(r => (r.SharedAt ?? "").StartsWith(todayPrefix));
        var totalUniqueVehicles = allRecords.Where(r => !string.IsNullOrWhiteSpace(r.VehicleNo))
            .Select(r => r.VehicleNo.Trim().ToUpperInvariant())
            .Distinct()
            .Count();
        var activeSendersCount = allRecords.Where(r => !string.IsNullOrWhiteSpace(r.SharedBy))
            .Select(r => r.SharedBy.Trim().ToLowerInvariant())
            .Distinct()
            .Count();

        IEnumerable<DataShareRecord> filtered = allRecords;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var cleanedQuery = CleanHiddenSpaces(query);
            var terms = cleanedQuery.Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(CleanHiddenSpaces)
                                    .Where(t => !string.IsNullOrEmpty(t))
                                    .ToList();
            filtered = filtered.Where(r =>
            {
                var combined = CleanHiddenSpaces(string.Join(" ",
                    r.VehicleNo, r.Imei, r.Iccid, r.DeviceName, r.DeviceBranch,
                    r.SharedBy, r.SharedByEmail, r.SharedByRole, r.Recipient, r.Notes));

                var compactCombined = CompactAlphaNumeric(combined);

                return terms.All(t =>
                {
                    if (combined.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
                    var compactT = CompactAlphaNumeric(t);
                    return !string.IsNullOrEmpty(compactT) && compactCombined.Contains(compactT, StringComparison.OrdinalIgnoreCase);
                });
            });
        }

        var list = filtered.ToList();
        var totalItems = list.Count;
        var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return View(new DataShareHistoryViewModel
        {
            Query = query,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            Records = paged,
            TotalSharesToday = totalSharesToday,
            TotalUniqueVehicles = totalUniqueVehicles,
            ActiveSendersCount = activeSendersCount
        });
    }
}

