using Microsoft.AspNetCore.Authorization;







using Microsoft.AspNetCore.Mvc;







using Microsoft.EntityFrameworkCore;







using MygtsMvc.Models;







using MygtsMvc.Services;







using MygtsMvc.ViewModels;







using System.Security.Claims;















namespace MygtsMvc.Areas.DMG.Controllers;







using Microsoft.AspNetCore.Mvc;















[Area("DMG")]







[Authorize]







[RequireModulePermission("Devices", PermissionLevel.View)]







public sealed class DevicesController : Controller







{







    private readonly IAppDataStore _store;







    private readonly SpreadsheetService _spreadsheets;
    private readonly MygtsMvc.Data.ApplicationDbContext _db;















    public DevicesController(IAppDataStore store, SpreadsheetService spreadsheets, MygtsMvc.Data.ApplicationDbContext db)
    {
        _store = store;
        _spreadsheets = spreadsheets;
        _db = db;







    }















    public static bool IsValidVehicleNumber(string? veh, string? vendor = null)
    {
        if (string.IsNullOrWhiteSpace(veh)) return false;
        var clean = veh.Replace(" ", "").Trim();
        if (clean.Equals("Not In Use", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("NotInUse", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Inventory", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(vendor) && clean.Equals(vendor.Replace(" ", "").Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!clean.Any(char.IsDigit))
            return false;

        return true;
    }

    public static bool GetIsPaid(DeviceRecord d, HashSet<string> paidVehicles, HashSet<string>? paidImeis = null)
    {
        if (string.Equals(d.Paid, "Yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(d.Paid, "No", StringComparison.OrdinalIgnoreCase)) return false;

        if (paidImeis != null && !string.IsNullOrWhiteSpace(d.Imei) && paidImeis.Contains(d.Imei.Trim()))
            return true;

        if (!string.IsNullOrWhiteSpace(d.VehicleNo))
        {
            var clean = d.VehicleNo.Replace(" ", "").Trim();
            if (IsValidVehicleNumber(clean, d.Vendor))
            {
                return paidVehicles.Contains(clean);
            }
        }
        return false;
    }















    private IEnumerable<DeviceRecord> FilterAndSortDevices(AppData data, DeviceFilterViewModel filters, string activeBranch)
    {
        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        IEnumerable<DeviceRecord> devices = data.Devices;

        var txnVehicles = data.Transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.VehicleNo))
            .Select(t => t.VehicleNo.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var paidVehicles = data.Transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.VehicleNo) 
                     && IsValidVehicleNumber(t.VehicleNo, t.Vendor)
                     && (string.Equals(t.PaymentFor, "Activation", StringComparison.OrdinalIgnoreCase)
                         || t.PaymentFor.Contains("Activation", StringComparison.OrdinalIgnoreCase)
                         || t.PaymentFor.Contains("Device", StringComparison.OrdinalIgnoreCase))
                     && (t.Amount ?? 0) > 0)
            .Select(t => t.VehicleNo.Replace(" ", "").Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var paidImeis = data.Transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.Imei)
                     && (string.Equals(t.PaymentFor, "Activation", StringComparison.OrdinalIgnoreCase)
                         || t.PaymentFor.Contains("Activation", StringComparison.OrdinalIgnoreCase)
                         || t.PaymentFor.Contains("Device", StringComparison.OrdinalIgnoreCase))
                     && (t.Amount ?? 0) > 0)
            .Select(t => t.Imei!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(filters.Paid))
        {
            if (string.Equals(filters.Paid, "Yes", StringComparison.OrdinalIgnoreCase))
                devices = devices.Where(device => GetIsPaid(device, paidVehicles, paidImeis));
            else if (string.Equals(filters.Paid, "No", StringComparison.OrdinalIgnoreCase))
                devices = devices.Where(device => !GetIsPaid(device, paidVehicles, paidImeis));
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
                case "Unpaid":
                    devices = devices.Where(device => !string.IsNullOrWhiteSpace(device.VehicleNo) 
                        && !device.ExcludeFromUnpaid 
                        && (string.IsNullOrWhiteSpace(device.InvoiceId) || !device.InvoiceId.StartsWith("old", StringComparison.OrdinalIgnoreCase))
                        && !txnVehicles.Contains(device.VehicleNo.Trim()));
                    break;
            }
        }
        else
        {
            bool explicitInventoryFilter = string.Equals(filters.Lifecycle, "Inventory", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(filters.Stock, "New", StringComparison.OrdinalIgnoreCase);

            if (!explicitInventoryFilter)
            {
                devices = devices.Where(device =>
                    !string.IsNullOrWhiteSpace(device.VehicleNo) &&
                    !string.Equals(device.VehicleNo.Trim(), "Inventory", StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var terms = filters.Query.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries);

            devices = devices.Where(device =>
            {
                var fields = new[]
                {
                    device.SlNo, device.DeviceSerialNo, device.VehicleNo, device.DeviceName, device.Imei, device.Iccid,
                    device.SimAirtel, device.SimBsnl, device.Vendor, device.Owner,
                    device.InvoiceId, device.EngineNo, device.ChasisNo, device.CellNo,
                    device.VehicleMake, device.VehicleModel, device.Rto, device.Address,
                    device.ProofOfAddress, device.DocumentNumber,
                    device.Notes, device.PreviousImei, device.HardwareHistory,
                    device.Type, device.LifecycleStatus, device.Status, device.StockStatus,
                    DateValue.Short(device.LatestExpiryDate), DateValue.Short(device.ActivationDate), DateValue.Short(device.CreatedAt)
                };

                return terms.All(term =>
                {
                    var compactT = CompactAlphaNumeric(term);

                    foreach (var f in fields)
                    {
                        if (string.IsNullOrWhiteSpace(f)) continue;
                        if (f.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
                        if (!string.IsNullOrEmpty(compactT) && CompactAlphaNumeric(f).Contains(compactT, StringComparison.OrdinalIgnoreCase)) return true;
                    }

                    return false;
                });
            });
        }

        if (!string.IsNullOrWhiteSpace(filters.Vendor)) devices = devices.Where(device => string.Equals(device.Vendor, filters.Vendor, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filters.NetworkOperator)) devices = devices.Where(device => string.Equals(device.NetworkOperator, filters.NetworkOperator, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filters.Lifecycle)) devices = devices.Where(device => string.Equals(device.LifecycleStatus, filters.Lifecycle, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filters.Stock)) devices = devices.Where(device => string.Equals(device.StockStatus, filters.Stock, StringComparison.OrdinalIgnoreCase));

        // Date Range Filtering (Expiry Date, Activation Date, or All)
        if (!string.IsNullOrWhiteSpace(filters.FromDate) || !string.IsNullOrWhiteSpace(filters.ToDate))
        {
            var dateType = filters.DateType?.Trim();
            var isActivation = string.Equals(dateType, "Activation", StringComparison.OrdinalIgnoreCase);
            var isExpiry = string.Equals(dateType, "Expiry", StringComparison.OrdinalIgnoreCase);
            var from = !string.IsNullOrWhiteSpace(filters.FromDate) ? filters.FromDate.Trim() : null;
            var to = !string.IsNullOrWhiteSpace(filters.ToDate) ? filters.ToDate.Trim() : null;

            static bool DateInRange(string? targetDate, string? f, string? t)
            {
                if (string.IsNullOrWhiteSpace(targetDate)) return false;
                var dStr = targetDate.Trim();
                if (DateTime.TryParse(dStr, out var parsedDt))
                {
                    dStr = parsedDt.ToString("yyyy-MM-dd");
                }
                if (f != null && string.Compare(dStr, f, StringComparison.Ordinal) < 0) return false;
                if (t != null && string.Compare(dStr, t, StringComparison.Ordinal) > 0) return false;
                return true;
            }

            devices = devices.Where(device =>
            {
                if (isActivation) return DateInRange(device.ActivationDate, from, to);
                if (isExpiry) return DateInRange(device.LatestExpiryDate, from, to);
                return DateInRange(device.LatestExpiryDate, from, to) || DateInRange(device.ActivationDate, from, to);
            });
        }

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
            "make" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.VehicleMake).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.VehicleMake).ThenByDescending(d => d.SlNoSortable),
            "model" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.VehicleModel).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.VehicleModel).ThenByDescending(d => d.SlNoSortable),
            "rto" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.Rto).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.Rto).ThenByDescending(d => d.SlNoSortable),
            "address" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.Address).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.Address).ThenByDescending(d => d.SlNoSortable),
            "docno" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.DocumentNumber).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.DocumentNumber).ThenByDescending(d => d.SlNoSortable),
            "cdacc" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.CdacCertificate).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.CdacCertificate).ThenByDescending(d => d.SlNoSortable),
            "vldtc" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.VldtCertificate).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.VldtCertificate).ThenByDescending(d => d.SlNoSortable),
            "deviceserial" or "serial" => filters.SortDirection == "asc" ? devices.OrderBy(d => d.DeviceSerialNo).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => d.DeviceSerialNo).ThenByDescending(d => d.SlNoSortable),
            "paid" => filters.SortDirection == "asc" ? devices.OrderBy(d => GetIsPaid(d, paidVehicles, paidImeis)).ThenByDescending(d => d.SlNoSortable) : devices.OrderByDescending(d => GetIsPaid(d, paidVehicles, paidImeis)).ThenByDescending(d => d.SlNoSortable),
            _ => devices.OrderByDescending(d => d.SlNoSortable)
        };

        return devices;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DeviceFilterViewModel filters, [FromQuery] bool clear = false)
    {
        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var filterCookieKey = $"mygts_filter_{activeBranch}_devices";

        if (clear)
        {
            Response.Cookies.Delete(filterCookieKey);
            filters = new DeviceFilterViewModel
            {
                Page = 1,
                PageSize = filters.PageSize > 0 ? filters.PageSize : 20
            };
        }
        else
        {
            var hasExplicitFilters = Request.Query.Keys.Any(k =>
                !string.Equals(k, "clear", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(k, "thisPageOnly", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(Request.Query[k]));

            if (hasExplicitFilters)
            {
                var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(filters.Query)) dict["Query"] = filters.Query;
                if (!string.IsNullOrWhiteSpace(filters.Vendor)) dict["Vendor"] = filters.Vendor;
                if (!string.IsNullOrWhiteSpace(filters.NetworkOperator)) dict["NetworkOperator"] = filters.NetworkOperator;
                if (!string.IsNullOrWhiteSpace(filters.Lifecycle)) dict["Lifecycle"] = filters.Lifecycle;
                if (!string.IsNullOrWhiteSpace(filters.Stock)) dict["Stock"] = filters.Stock;
                if (!string.IsNullOrWhiteSpace(filters.SpecialFilter)) dict["SpecialFilter"] = filters.SpecialFilter;
                if (!string.IsNullOrWhiteSpace(filters.Paid)) dict["Paid"] = filters.Paid;
                if (!string.IsNullOrWhiteSpace(filters.FromDate)) dict["FromDate"] = filters.FromDate;
                if (!string.IsNullOrWhiteSpace(filters.ToDate)) dict["ToDate"] = filters.ToDate;
                if (!string.IsNullOrWhiteSpace(filters.DateType)) dict["DateType"] = filters.DateType;
                if (!string.IsNullOrWhiteSpace(filters.SortColumn)) dict["SortColumn"] = filters.SortColumn;
                if (!string.IsNullOrWhiteSpace(filters.SortDirection)) dict["SortDirection"] = filters.SortDirection;
                if (filters.Page > 1) dict["Page"] = filters.Page.ToString();
                if (filters.PageSize != 20) dict["PageSize"] = filters.PageSize.ToString();

                if (dict.Count > 0)
                {
                    Response.Cookies.Append(filterCookieKey, System.Text.Json.JsonSerializer.Serialize(dict), new CookieOptions
                    {
                        Path = "/",
                        Expires = DateTimeOffset.UtcNow.AddDays(7),
                        SameSite = SameSiteMode.Lax,
                        HttpOnly = false
                    });
                }
                else
                {
                    Response.Cookies.Delete(filterCookieKey);
                }
            }
            else
            {
                if (Request.Cookies.TryGetValue(filterCookieKey, out var savedJson) && !string.IsNullOrWhiteSpace(savedJson))
                {
                    try
                    {
                        var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(savedJson);
                        if (saved is not null && saved.Count > 0)
                        {
                            var routeVals = new RouteValueDictionary();
                            foreach (var kvp in saved)
                            {
                                routeVals[kvp.Key] = kvp.Value;
                            }
                            return RedirectToAction(nameof(Index), routeVals);
                        }
                    }
                    catch
                    {
                        Response.Cookies.Delete(filterCookieKey);
                    }
                }
            }
        }

        var data = await _store.SnapshotAsync();
        var sortedDevices = FilterAndSortDevices(data, filters, activeBranch);
        var sortedDevicesList = sortedDevices.ToList();
        var totalItems = sortedDevicesList.Count;
        var pagedDevices = (filters.PageSize >= 1000000 || filters.PageSize <= 0) 
            ? sortedDevicesList 
            : sortedDevicesList.Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize).ToList();

        var paidVehicles = data.Transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.VehicleNo) 
                     && IsValidVehicleNumber(t.VehicleNo, t.Vendor)
                     && (string.Equals(t.PaymentFor, "Activation", StringComparison.OrdinalIgnoreCase)
                         || t.PaymentFor.Contains("Activation", StringComparison.OrdinalIgnoreCase)
                         || t.PaymentFor.Contains("Device", StringComparison.OrdinalIgnoreCase))
                     && (t.Amount ?? 0) > 0)
            .Select(t => t.VehicleNo.Replace(" ", "").Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var paidImeis = data.Transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.Imei)
                     && (string.Equals(t.PaymentFor, "Activation", StringComparison.OrdinalIgnoreCase)
                         || t.PaymentFor.Contains("Activation", StringComparison.OrdinalIgnoreCase)
                         || t.PaymentFor.Contains("Device", StringComparison.OrdinalIgnoreCase))
                     && (t.Amount ?? 0) > 0)
            .Select(t => t.Imei!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ViewBag.PaidVehicles = paidVehicles;
        ViewBag.PaidImeis = paidImeis;

        // [Title: Batch Color Mapping - Assigns each distinct batch in the branch a unique color]
        ViewBag.BatchColorMap = BatchColorHelper.BuildBatchColorMap(data.Devices.Select(d => d.InvoiceId));

        return View(new DeviceFilterViewModel
        {
            Query = filters.Query,
            Vendor = filters.Vendor,
            NetworkOperator = filters.NetworkOperator,
            Lifecycle = filters.Lifecycle,
            Stock = filters.Stock,
            Paid = filters.Paid,
            FromDate = filters.FromDate,
            ToDate = filters.ToDate,
            DateType = filters.DateType,
            SortColumn = filters.SortColumn,
            SortDirection = filters.SortDirection,
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalItems = totalItems,
            Devices = pagedDevices,
            Vendors = data.Devices.Where(d => string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch)).Select(device => device.Vendor).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
            NetworkOperators = data.Devices.Where(d => string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch)).Select(device => device.NetworkOperator).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
            LifecycleOptions = data.Devices.Where(d => string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch)).Select(device => device.LifecycleStatus).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
            StockOptions = data.Devices.Where(d => string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(d.Branch)).Select(device => device.StockStatus).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList()
        });
    }

    [HttpGet]







    [RequireModulePermission("Devices", PermissionLevel.Edit)]
    public async Task<IActionResult> Create(string? returnUrl = null, bool forceBlank = false)
    {
        ViewBag.ReturnUrl = returnUrl;
        var data = await _store.SnapshotAsync();
        const string currentBranch = "DMG";

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















        if (!User.IsExecutive())







        {







            var existingDevice = dataSnapshot.Devices.FirstOrDefault(d => d.Id == model.Id);







            if (existingDevice != null && existingDevice.IsNotInUse)







            {







                model.IsMuted = true;







                // model.VehicleNo preserved







            }







        }







        else if (model.IsMuted || string.Equals(model.VehicleNo?.Trim(), "Not In Use", StringComparison.OrdinalIgnoreCase))







        {







            model.IsMuted = true;







            // model.VehicleNo preserved







        }







        







        if (!string.IsNullOrWhiteSpace(model.SlNo) && dataSnapshot.Devices.Any(d => d.SlNo == model.SlNo))







        {







            ModelState.AddModelError("SlNo", "This serial number is already given to another device.");







        }















        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";







        var config = DeviceLifecycleHelper.BuildMergedConfig(activeBranch, dataSnapshot.Config);







        if (model.ActivationDate.HasValue && !model.LatestExpiryDate.HasValue)







        {







            var calcExp = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate.Value.ToString("yyyy-MM-dd"), activeBranch, model.Type, config);







            if (DateValue.Parse(calcExp) is DateTime dt) model.LatestExpiryDate = dt;







        }















        if (!ModelState.IsValid)







        {







            ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "12";







            ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "0";







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







            if (device.AdminCost == 0m)







            {







                var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);







                if (config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc) && parsedAc > 0m)







                {







                    device.AdminCost = parsedAc;







                }







            }







            data.Devices.Add(device);







        });







        TempData["Success"] = $"Device {device.VehicleNo} was added to the fleet.";

        if (!string.IsNullOrWhiteSpace(device.InvoiceId))
        {
            var batchUrl = $"/DMG/Inventory?expandBatch={Uri.EscapeDataString(device.InvoiceId)}&highlightDevice={device.Id}";
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
                return Redirect("/DMG/Inventory");
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















        if (!User.IsExecutive())







        {







            var existingDevice = dataSnapshot.Devices.FirstOrDefault(d => d.Id == model.Id);







            if (existingDevice != null && existingDevice.IsNotInUse)







            {







                model.IsMuted = true;







                // model.VehicleNo preserved







            }







        }







        else if (model.IsMuted || string.Equals(model.VehicleNo?.Trim(), "Not In Use", StringComparison.OrdinalIgnoreCase))







        {







            model.IsMuted = true;







            // model.VehicleNo preserved







        }















        if (!string.IsNullOrWhiteSpace(model.SlNo) && dataSnapshot.Devices.Any(d => d.SlNo == model.SlNo && d.Id != model.Id))







        {







            ModelState.AddModelError("SlNo", "This serial number is already given to another device.");







        }















        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";







        var config = DeviceLifecycleHelper.BuildMergedConfig(activeBranch, dataSnapshot.Config);















        // Auto-calculate expiry date if activation date is present but expiry date is missing







        if (model.ActivationDate.HasValue && !model.LatestExpiryDate.HasValue)







        {







            var calcExp = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate.Value.ToString("yyyy-MM-dd"), activeBranch, model.Type, config);







            if (DateValue.Parse(calcExp) is DateTime dt) model.LatestExpiryDate = dt;







        }















        if (!ModelState.IsValid)







        {







            ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "12";







            ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "0";







            ViewBag.ReturnUrl = returnUrl;







            return View(model);







        }







        var updated = false;







        await _store.UpdateAsync(data =>







        {







            var device = data.Devices.FirstOrDefault(item => item.Id == model.Id);







            if (device is null) return;







            Copy(model, device);

            // Synchronize updated dates across matching Transactions and Recharges
            var cleanDevVeh = (device.VehicleNo ?? "").Replace(" ", "").Trim();
            var cleanDevImei = (device.Imei ?? "").Trim();
            var cleanDevIccid = (device.Iccid ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(device.LatestExpiryDate) || !string.IsNullOrWhiteSpace(device.ActivationDate))
            {
                if (data.Transactions != null)
                {
                    var txns = data.Transactions.Where(t =>
                        (!string.IsNullOrWhiteSpace(cleanDevImei) && string.Equals((t.Imei ?? "").Trim(), cleanDevImei, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(cleanDevVeh) && string.Equals((t.VehicleNo ?? "").Replace(" ", "").Trim(), cleanDevVeh, StringComparison.OrdinalIgnoreCase))
                    ).ToList();
                    foreach (var t in txns)
                    {
                        if (!string.IsNullOrWhiteSpace(device.LatestExpiryDate)) t.NewExpiryDate = device.LatestExpiryDate;
                        if (!string.IsNullOrWhiteSpace(device.ActivationDate)) t.ActivationDate = device.ActivationDate;
                    }
                }

                if (data.Recharges != null)
                {
                    var recs = data.Recharges.Where(r =>
                        (!string.IsNullOrWhiteSpace(cleanDevImei) && string.Equals((r.Imei ?? "").Trim(), cleanDevImei, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(cleanDevIccid) && string.Equals((r.Iccid ?? "").Trim(), cleanDevIccid, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(cleanDevVeh) && string.Equals((r.VehicleNo ?? "").Replace(" ", "").Trim(), cleanDevVeh, StringComparison.OrdinalIgnoreCase))
                    ).ToList();
                    foreach (var r in recs)
                    {
                        if (!string.IsNullOrWhiteSpace(device.LatestExpiryDate)) r.ExpiryDate = device.LatestExpiryDate;
                        if (!string.IsNullOrWhiteSpace(device.ActivationDate)) r.ActivationDate = device.ActivationDate;
                    }
                }
            }







            if (!string.IsNullOrWhiteSpace(device.InvoiceId))







            {







                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, device.InvoiceId, StringComparison.OrdinalIgnoreCase));







                if (invoice != null) device.UnitCost = invoice.CostPerDevice;







            }







            updated = true;







        });







        if (!updated) return NotFound();







        TempData["Success"] = $"Device {model.VehicleNo} updated.";

        if (!string.IsNullOrWhiteSpace(model.InvoiceId))
        {
            var batchUrl = $"/DMG/Inventory?expandBatch={Uri.EscapeDataString(model.InvoiceId)}&highlightDevice={model.Id}";
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
                return Redirect("/DMG/Inventory");
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







    [IgnoreAntiforgeryToken]







    public async Task<IActionResult> TogglePaid([FromBody] TogglePaidRequest request)







    {







        if (!User.IsExecutive())







        {







            return StatusCode(403, new { error = "Only executives or administrators can change device payment status." });







        }







        if (request == null || string.IsNullOrWhiteSpace(request.DeviceId))







        {







            return BadRequest(new { error = "Invalid device ID." });







        }















        var data = await _store.SnapshotAsync();







        var targetDevice = data.Devices.FirstOrDefault(d => d.Id == request.DeviceId);







        if (targetDevice == null)







        {







            return NotFound(new { error = "Device not found." });







        }















        var paidVehicles = data.Transactions







            .Where(t => !string.IsNullOrWhiteSpace(t.VehicleNo) 







                     && (string.Equals(t.PaymentFor, "Activation", StringComparison.OrdinalIgnoreCase)







                         || t.PaymentFor.Contains("Activation", StringComparison.OrdinalIgnoreCase)







                         || t.PaymentFor.Contains("Device", StringComparison.OrdinalIgnoreCase))







                     && (t.Amount ?? 0) > 0)







            .Select(t => t.VehicleNo.Replace(" ", "").Trim())







            .ToHashSet(StringComparer.OrdinalIgnoreCase);















        var currentlyPaid = GetIsPaid(targetDevice, paidVehicles);







        var targetPaid = !string.IsNullOrWhiteSpace(request.TargetStatus)







            ? string.Equals(request.TargetStatus, "Yes", StringComparison.OrdinalIgnoreCase)







            : !currentlyPaid;















        var newStatusStr = targetPaid ? "Yes" : "No";















        await _store.UpdateDeviceAsync(request.DeviceId, device =>







        {







            device.Paid = newStatusStr;







        });















        return Json(new { success = true, isPaid = targetPaid, paidText = newStatusStr, deviceId = request.DeviceId });







    }















    [HttpPost]







    [IgnoreAntiforgeryToken]







    [RequireModulePermission("Devices", PermissionLevel.Edit)]







    public async Task<IActionResult> ReplaceVehicle([FromBody] ReplaceVehicleRequest request)







    {







        if (request == null || string.IsNullOrWhiteSpace(request.DeviceId))







        {







            return BadRequest(new { success = false, message = "Device ID is required." });







        }







        if (string.IsNullOrWhiteSpace(request.NewVehicleNo))







        {







            return BadRequest(new { success = false, message = "New vehicle number is required." });







        }















        var data = await _store.SnapshotAsync();







        var targetDevice = data.Devices.FirstOrDefault(d => d.Id == request.DeviceId);







        if (targetDevice == null)







        {







            return NotFound(new { success = false, message = "Device not found." });







        }















        var timestamp = DateTime.UtcNow.ToString("dd MMM yyyy");







        var prevDetails = new List<string>();







        if (!string.IsNullOrWhiteSpace(targetDevice.VehicleNo)) prevDetails.Add($"Veh: {targetDevice.VehicleNo.Trim()}");







        if (!string.IsNullOrWhiteSpace(targetDevice.Owner)) prevDetails.Add($"Owner: {targetDevice.Owner.Trim()}");







        if (!string.IsNullOrWhiteSpace(targetDevice.Vendor)) prevDetails.Add($"Vendor: {targetDevice.Vendor.Trim()}");







        if (!string.IsNullOrWhiteSpace(targetDevice.CellNo)) prevDetails.Add($"Contact: {targetDevice.CellNo.Trim()}");







        if (!string.IsNullOrWhiteSpace(targetDevice.EngineNo)) prevDetails.Add($"Eng: {targetDevice.EngineNo.Trim()}");







        if (!string.IsNullOrWhiteSpace(targetDevice.ChasisNo)) prevDetails.Add($"Chas: {targetDevice.ChasisNo.Trim()}");







        if (!string.IsNullOrWhiteSpace(targetDevice.ProofOfAddress) && !string.IsNullOrWhiteSpace(targetDevice.DocumentNumber))







        {







            prevDetails.Add($"{targetDevice.ProofOfAddress.Trim()}: {targetDevice.DocumentNumber.Trim()}");







        }















        var historyLog = $"Replaced on {timestamp} from [ {string.Join(", ", prevDetails)} ]";







        if (!string.IsNullOrWhiteSpace(request.ReplacementNotes))







        {







            historyLog += $" (Note: {request.ReplacementNotes.Trim()})";







        }















        var snapshotObj = new







        {







            PreviousVehicleNo = targetDevice.VehicleNo,







            PreviousOwner = targetDevice.Owner,







            PreviousVendor = targetDevice.Vendor,







            PreviousCellNo = targetDevice.CellNo,







            PreviousEngineNo = targetDevice.EngineNo,







            PreviousChasisNo = targetDevice.ChasisNo,







            PreviousProofOfAddress = targetDevice.ProofOfAddress,







            PreviousDocumentNumber = targetDevice.DocumentNumber,







            PreviousAddress = targetDevice.Address,







            ReplacedAt = DateTime.UtcNow.ToString("O"),







            Notes = request.ReplacementNotes







        };







        var snapshotJson = System.Text.Json.JsonSerializer.Serialize(snapshotObj);















        await _store.UpdateDeviceAsync(request.DeviceId, device =>







        {







            // Update Hardware History (append-only)







            device.HardwareHistory = string.IsNullOrWhiteSpace(device.HardwareHistory)







                ? historyLog







                : $"{device.HardwareHistory} | {historyLog}";















            device.ReplacedDeviceSnapshot = snapshotJson;















            // Clear old ownership details & set new details







            device.VehicleNo = (request.NewVehicleNo ?? "").Trim();







            device.Vendor = (request.NewVendor ?? "").Trim();







            device.Owner = (request.NewOwner ?? "").Trim();







            device.CellNo = (request.NewCellNo ?? "").Trim();







            device.EngineNo = (request.NewEngineNo ?? "").Trim();







            device.ChasisNo = (request.NewChasisNo ?? "").Trim();







            device.ProofOfAddress = (request.NewProofOfAddress ?? "").Trim();







            device.DocumentNumber = (request.NewDocumentNumber ?? "").Trim();







            device.Address = (request.NewAddress ?? "").Trim();







            device.InstallType = "Replacement";















            // If replacement notes provided, append to Notes field







            if (!string.IsNullOrWhiteSpace(request.ReplacementNotes))







            {







                var noteLine = $"[Replacement {timestamp}]: {request.ReplacementNotes.Trim()}";







                device.Notes = string.IsNullOrWhiteSpace(device.Notes)







                    ? noteLine







                    : $"{device.Notes}\n{noteLine}";







            }















            // CRITICAL: All device hardware details, dates, and lifecycle remain untouched







        });















        return Json(new







        {







            success = true,







            message = $"Device successfully transferred to {request.NewVehicleNo.Trim()}.",







            deviceId = request.DeviceId,







            newVehicleNo = request.NewVehicleNo.Trim()







        });







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

    [HttpGet("/DMG/Devices/ExportAll")]

    [HttpPost("/DMG/Devices/ExportAll")]

    [IgnoreAntiforgeryToken]







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















    [HttpGet("Export")]
    [HttpGet("~/DMG/Devices/Export")]
    public async Task<IActionResult> Export()
    {
        var data = await _store.SnapshotAsync();
        var bytes = _spreadsheets.Export(data, "DMG");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"mygts-export-dmg-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("~/Devices/DownloadSample")]

    [HttpGet("/DMG/Devices/DownloadSample")]







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







        var existingDevices = _db.Devices.IgnoreQueryFilters().AsNoTracking().ToList();















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

                    if (existing == null)
                    {
                        var crossBranchDev = _db.Devices.IgnoreQueryFilters().FirstOrDefault(d =>
                            !string.IsNullOrWhiteSpace(d.Imei) && !string.IsNullOrWhiteSpace(staged.Imei) &&
                            !string.IsNullOrWhiteSpace(d.Iccid) && !string.IsNullOrWhiteSpace(staged.Iccid) &&
                            string.Equals(d.Imei.Trim(), staged.Imei.Trim(), StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(d.Iccid.Trim(), staged.Iccid.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (crossBranchDev != null)
                        {
                            crossBranchDev.Branch = currentBranch;
                            current.Devices.Add(crossBranchDev);
                            existing = crossBranchDev;
                        }
                    }















                    if (existing != null)
                    {
                        existing.Branch = currentBranch;
                        if (!string.IsNullOrWhiteSpace(staged.SlNo)) existing.SlNo = staged.SlNo.Trim();
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







        record.SlNo = (model.SlNo ?? "").Trim(); record.DeviceSerialNo = (model.DeviceSerialNo ?? "").Trim(); record.VehicleNo = cleanVeh; record.DeviceName = SpreadsheetService.NormalizeDeviceName(model.DeviceName); record.InstallType = model.InstallType; record.Type = SpreadsheetService.NormalizeType(model.Type); record.Vendor = (model.Vendor ?? "").Trim(); record.Owner = (model.Owner ?? "").Trim(); record.CellNo = (model.CellNo ?? "").Trim(); record.RegistrationDate = model.RegistrationDate?.ToString("yyyy-MM-dd") ?? ""; record.Imei = (model.Imei ?? "").Trim(); record.Iccid = (model.Iccid ?? "").Trim(); record.SimAirtel = (model.SimAirtel ?? "").Trim(); record.SimBsnl = (model.SimBsnl ?? "").Trim(); record.ActivationDate = model.ActivationDate?.ToString("yyyy-MM-dd") ?? ""; record.LatestExpiryDate = model.LatestExpiryDate?.ToString("yyyy-MM-dd") ?? ""; record.LifecycleStatus = string.IsNullOrWhiteSpace(model.LifecycleStatus) ? "Active" : model.LifecycleStatus; record.Status = record.LifecycleStatus;
        if (!string.IsNullOrWhiteSpace(model.InvoiceId)) record.InvoiceId = model.InvoiceId.Trim();







        if (!string.IsNullOrWhiteSpace(cleanVeh))







        {







            record.StockStatus = "Used";







        }







        else







        {







            record.LifecycleStatus = "Inventory";







            record.Status = "Inventory";







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







        record.HardwareHistory = hh; record.InvoiceId = (model.InvoiceId ?? "").Trim(); record.ReplacementSource = model.ReplacementSource; record.ReplacedDeviceId = model.ReplacedDeviceId; record.ReplacedDeviceSnapshot = model.ReplacedDeviceSnapshot; record.ExcludeFromUnpaid = model.ExcludeFromUnpaid; record.IsMuted = model.IsMuted || string.Equals(cleanVeh, "Not In Use", StringComparison.OrdinalIgnoreCase); if (record.IsMuted) { record.Status = "Not In Use"; record.LifecycleStatus = "Not In Use"; } record.NetworkOperator = (model.NetworkOperator ?? "").Trim();







        record.CdacCertificate = string.IsNullOrWhiteSpace(model.CdacCertificate) ? "NO" : model.CdacCertificate.Trim().ToUpper();







        record.VldtCertificate = string.IsNullOrWhiteSpace(model.VldtCertificate) ? "NO" : model.VldtCertificate.Trim().ToUpper();







        record.Paid = string.Equals(model.Paid, "Yes", StringComparison.OrdinalIgnoreCase) ? "Yes"
                    : string.Equals(model.Paid, "No", StringComparison.OrdinalIgnoreCase) ? "No"
                    : "";







        if (model.AdminCost.HasValue) record.AdminCost = model.AdminCost.Value;







    }















    private static DeviceFormViewModel ToModel(DeviceRecord device, Dictionary<string, string>? config = null)







    {







        var registrationDate = DateValue.Parse(device.RegistrationDate);







        var activationDate = DateValue.Parse(device.ActivationDate);







        var expiryDate = DateValue.Parse(device.LatestExpiryDate);















        if (activationDate.HasValue && !expiryDate.HasValue)







        {







            var calculated = DeviceLifecycleHelper.CalculateExpiryFromActivation(device.ActivationDate, device.Branch, device.Type ?? device.InstallType, config);







            expiryDate = DateValue.Parse(calculated);







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







            IsMuted = device.IsNotInUse,







            NetworkOperator = device.NetworkOperator,







            CdacCertificate = string.IsNullOrWhiteSpace(device.CdacCertificate) ? "NO" : device.CdacCertificate,







            VldtCertificate = string.IsNullOrWhiteSpace(device.VldtCertificate) ? "NO" : device.VldtCertificate,







            Paid = device.Paid?.Trim() ?? "",







            AdminCost = device.AdminCost







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







    [HttpGet]







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















        IEnumerable<DeviceRecord> results = allDevices.Where(d => !d.IsNotInUse);















        if (!string.IsNullOrWhiteSpace(query))







        {







            var cleanedQuery = CleanHiddenSpaces(query);







            var rawTerms = cleanedQuery.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);







            var terms = rawTerms.Select(CleanHiddenSpaces).Where(t => !string.IsNullOrEmpty(t)).ToList();















            results = results.Where(d =>







            {







                var fields = new[]







                {







                    d.SlNo,







                    d.VehicleNo, d.Iccid, d.Imei, d.SimAirtel, d.SimBsnl,







                    d.DeviceName, d.NetworkOperator, d.Vendor, d.Owner, d.CellNo,







                    d.EngineNo, d.ChasisNo,







                    DateValue.Short(d.LatestExpiryDate)







                };















                return terms.All(t =>







                {







                    var compactT = CompactAlphaNumeric(t);















                    foreach (var f in fields)







                    {







                        if (string.IsNullOrWhiteSpace(f)) continue;







                        if (f.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;







                        if (!string.IsNullOrEmpty(compactT) && CompactAlphaNumeric(f).Contains(compactT, StringComparison.OrdinalIgnoreCase)) return true;







                    }















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















    [HttpGet]







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







            var terms = cleanedQuery.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)







                                    .Select(CleanHiddenSpaces)







                                    .Where(t => !string.IsNullOrEmpty(t))







                                    .ToList();







            filtered = filtered.Where(r =>







            {







                var fields = new[]







                {







                    r.VehicleNo, r.Imei, r.Iccid, r.DeviceName, r.DeviceBranch,







                    r.SharedBy, r.SharedByEmail, r.SharedByRole, r.Recipient, r.Notes







                };















                return terms.All(t =>







                {







                    var compactT = CompactAlphaNumeric(t);















                    foreach (var f in fields)







                    {







                        if (string.IsNullOrWhiteSpace(f)) continue;







                        if (f.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;







                        if (!string.IsNullOrEmpty(compactT) && CompactAlphaNumeric(f).Contains(compactT, StringComparison.OrdinalIgnoreCase)) return true;







                    }















                    return false;







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















