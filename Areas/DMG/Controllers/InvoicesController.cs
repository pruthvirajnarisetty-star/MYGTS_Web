using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Areas.DMG.Controllers;
using Microsoft.AspNetCore.Mvc;

[Area("DMG")]
[Authorize]
[RequireModulePermission("Invoices", PermissionLevel.View)]
public sealed class InvoicesController : Controller
{
    private readonly IAppDataStore _store;
    private readonly SpreadsheetService _spreadsheets;
    private readonly MygtsMvc.Data.ApplicationDbContext _db;

    public InvoicesController(IAppDataStore store, SpreadsheetService spreadsheets, MygtsMvc.Data.ApplicationDbContext db)
    {
        _store = store;
        _spreadsheets = spreadsheets;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? query = null,
        [FromQuery] string? networkOperator = null,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null,
        [FromQuery] int? quantity = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool clear = false)
    {
        var activeBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var filterCookieKey = $"mygts_filter_{activeBranch}_invoices";

        if (clear)
        {
            Response.Cookies.Delete(filterCookieKey);
            query = null;
            networkOperator = null;
            fromDate = null;
            toDate = null;
            quantity = null;
            page = 1;
        }
        else
        {
            var hasExplicitFilters = Request.Query.Keys.Any(k =>
                !string.Equals(k, "clear", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(Request.Query[k]));

            if (hasExplicitFilters)
            {
                var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(query)) dict["query"] = query;
                if (!string.IsNullOrWhiteSpace(networkOperator)) dict["networkOperator"] = networkOperator;
                if (!string.IsNullOrWhiteSpace(fromDate)) dict["fromDate"] = fromDate;
                if (!string.IsNullOrWhiteSpace(toDate)) dict["toDate"] = toDate;
                if (quantity.HasValue && quantity.Value > 0) dict["quantity"] = quantity.Value.ToString();
                if (page > 1) dict["page"] = page.ToString();
                if (pageSize != 20) dict["pageSize"] = pageSize.ToString();

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
        var allInvoices = data.Invoices.OrderByDescending(item => DateValue.Parse(item.Date) ?? DateTime.MinValue).ToList();

        var availableOperators = allInvoices
            .Select(i => i.NetworkOperator?.Trim())
            .Where(op => !string.IsNullOrWhiteSpace(op))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(op => op)
            .Select(op => op!)
            .ToList();

        var availableQuantities = allInvoices
            .Select(i => i.Quantity)
            .Where(q => q > 0)
            .Distinct()
            .OrderBy(q => q)
            .ToList();

        var filtered = allInvoices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            filtered = filtered.Where(i => 
                (i.InvoiceNo ?? "").ToLowerInvariant().Contains(q) ||
                (i.DeviceType ?? "").ToLowerInvariant().Contains(q) ||
                (i.Notes ?? "").ToLowerInvariant().Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(networkOperator))
        {
            filtered = filtered.Where(i => 
                string.Equals(i.NetworkOperator?.Trim(), networkOperator.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (quantity.HasValue && quantity.Value > 0)
        {
            filtered = filtered.Where(i => i.Quantity == quantity.Value);
        }

        DateTime? fromDt = DateValue.Parse(fromDate);
        if (fromDt.HasValue)
        {
            filtered = filtered.Where(i => {
                var d = DateValue.Parse(i.Date);
                return d.HasValue && d.Value.Date >= fromDt.Value.Date;
            });
        }

        DateTime? toDt = DateValue.Parse(toDate);
        if (toDt.HasValue)
        {
            filtered = filtered.Where(i => {
                var d = DateValue.Parse(i.Date);
                return d.HasValue && d.Value.Date <= toDt.Value.Date;
            });
        }

        var invoices = filtered.ToList();

        var totalItems = invoices.Count;
        var totalValue = invoices.Sum(item => item.TotalWithTaxes);
        var totalUnits = invoices.Sum(item => item.Quantity);
        var totalExtras = invoices.Sum(item => item.CourierCharges);
        var totalBaseAmount = invoices.Sum(item => item.Amount);

        var pagedInvoices = (pageSize >= 1000000 || pageSize <= 0)
            ? invoices
            : invoices.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var devicesByInvoice = data.Devices
            .Where(d => !string.IsNullOrEmpty(d.InvoiceId))
            .GroupBy(d => d.InvoiceId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var modelList = pagedInvoices.Select(inv =>
        {
            var matchedDevices = devicesByInvoice.GetValueOrDefault(inv.InvoiceNo);
            if ((matchedDevices == null || matchedDevices.Count == 0) &&
                BatchColorHelper.RtoInvoiceToBatchMap.TryGetValue(inv.InvoiceNo, out var bName))
            {
                matchedDevices = devicesByInvoice.GetValueOrDefault(bName);
            }

            return new InvoiceIndexItemViewModel
            {
                Invoice = inv,
                Devices = (matchedDevices ?? new List<DeviceRecord>())
                    .OrderBy(d => d.StockStatus == "New" ? 0 : (d.StockStatus == "Used" || d.StockStatus == "Activated") ? 1 : 2)
                    .ThenBy(d => d.SlNoSortable)
                    .ToList()
            };
        }).ToList();

        ViewBag.GlobalIndices = data.Devices
            .Select((d, i) => new { d.Id, Index = i + 1 })
            .ToDictionary(x => x.Id, x => x.Index);

        return View(new InvoiceListViewModel
        {
            Query = query,
            NetworkOperator = networkOperator,
            FromDate = fromDate,
            ToDate = toDate,
            Quantity = quantity,
            NetworkOperators = availableOperators,
            Quantities = availableQuantities,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalValue = totalValue,
            TotalUnits = totalUnits,
            TotalExtras = totalExtras,
            TotalBaseAmount = totalBaseAmount,
            Invoices = modelList
        });
    }

    [HttpGet]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public async Task<IActionResult> Create(string mode = "single", bool addon = false, string? batch = null)
    {
        var data = await _store.SnapshotAsync();
        var existingBatches = data.Invoices
            .Select(i => i.InvoiceNo)
            .Concat(data.Devices.Select(d => d.InvoiceId))
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => int.TryParse(b, out var n) ? n : int.MaxValue)
            .ThenBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var vm = new InvoiceCreatePageViewModel
        {
            ActiveTab = mode,
            ExistingInvoices = existingBatches,
            SelectedAddonBatch = batch
        };

        if (addon && !string.IsNullOrWhiteSpace(batch))
        {
            vm.SingleModel.IsAddon = true;
            vm.SingleModel.ExistingInvoiceNo = batch;
            vm.SingleModel.InvoiceNo = batch;
            vm.MultipleModel.IsAddon = true;
            vm.MultipleModel.ExistingInvoiceNo = batch;
            vm.MultipleModel.InvoiceNo = batch;
        }

        return View(vm);
    }

    [HttpPost]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public async Task<IActionResult> CreateSingle([Bind(Prefix = "SingleModel")] InvoiceFormViewModel model)
    {
        var data = await _store.SnapshotAsync();
        var existingBatches = data.Invoices
            .Select(i => i.InvoiceNo)
            .Concat(data.Devices.Select(d => d.InvoiceId))
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => int.TryParse(b, out var n) ? n : int.MaxValue)
            .ThenBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invoiceNo = model.IsAddon ? model.ExistingInvoiceNo?.Trim() : model.InvoiceNo?.Trim();
        if (string.IsNullOrWhiteSpace(invoiceNo))
        {
            TempData["Error"] = model.IsAddon ? "Please select an existing batch for the add-on." : "Please enter a valid invoice number.";
            return View("Create", new InvoiceCreatePageViewModel { SingleModel = model, ActiveTab = "single", ExistingInvoices = existingBatches });
        }

        await _store.UpdateAsync(storeData =>
        {
            var existingInvoice = storeData.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, invoiceNo, StringComparison.OrdinalIgnoreCase));
            
            var pbQty = model.PanicButtonQuantity ?? 0;
            var pbTotal = pbQty * (model.PanicButtonCost ?? 0m);
            var isBharat101 = string.Equals(model.DeviceType, "Bharat101", StringComparison.OrdinalIgnoreCase);
            var effectiveRate = (model.Rate ?? 0m) + (isBharat101 && (model.Rate ?? 0m) < 3000 ? 1000m : 0m);
            var deviceAmount = model.Quantity * effectiveRate;
            var additionalAmount = deviceAmount + pbTotal + (model.CourierCharges ?? 0m);
            
            decimal costPerDevice;
            if (model.IsAddon && existingInvoice != null)
            {
                existingInvoice.Quantity += model.Quantity;
                if (pbQty > 0)
                {
                    existingInvoice.PanicButtonQuantity += pbQty;
                    existingInvoice.PanicButtonCost = model.PanicButtonCost ?? existingInvoice.PanicButtonCost;
                    existingInvoice.PanicButtonTotal += pbTotal;
                }
                if (additionalAmount > 0)
                {
                    existingInvoice.Amount += additionalAmount;
                    var newExactTotal = existingInvoice.Amount * 1.18m;
                    existingInvoice.TotalWithTaxes = Math.Round(newExactTotal, MidpointRounding.AwayFromZero);
                    existingInvoice.RoundOff = existingInvoice.TotalWithTaxes - newExactTotal;
                    existingInvoice.CostPerDevice = existingInvoice.Quantity > 0 ? existingInvoice.TotalWithTaxes / existingInvoice.Quantity : 0;
                }
                costPerDevice = existingInvoice.CostPerDevice;
            }
            else
            {
                var exactTotal = additionalAmount * 1.18m;
                var roundedTotal = Math.Round(exactTotal, MidpointRounding.AwayFromZero);
                var roundOff = roundedTotal - exactTotal;
                costPerDevice = model.Quantity > 0 ? roundedTotal / model.Quantity : 0;

                var invoice = new InvoiceRecord
                {
                    Date = model.Date.ToString("yyyy-MM-dd"),
                    DeviceType = string.IsNullOrWhiteSpace(model.DeviceType) ? "GPS" : model.DeviceType.Trim(),
                    InvoiceNo = invoiceNo,
                    Quantity = model.Quantity,
                    Rate = effectiveRate,
                    Amount = deviceAmount,
                    CourierCharges = model.CourierCharges ?? 0m,
                    TotalWithTaxes = roundedTotal,
                    RoundOff = roundOff,
                    CostPerDevice = costPerDevice,
                    Notes = (model.Notes ?? "").Trim(),
                    PanicButtonQuantity = pbQty,
                    PanicButtonCost = model.PanicButtonCost ?? 0m,
                    PanicButtonTotal = pbTotal,
                    NetworkOperator = (model.NetworkOperator ?? "").Trim()
                };
                storeData.Invoices.Add(invoice);
            }

            var maxSlNo = storeData.Devices.Count > 0 ? storeData.Devices.Max(d => int.TryParse(d.SlNo, out var val) ? val : 0) : 0;
            var devType = !string.IsNullOrWhiteSpace(model.DeviceType) && model.DeviceType != "None" ? model.DeviceType.Trim() : (existingInvoice?.DeviceType ?? "GPS Tracker");

            var config = storeData.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
            var invoiceAdminCost = (config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc)) ? parsedAc : 0m;

            for (int i = 0; i < model.Quantity; i++)
            {
                storeData.Devices.Add(new DeviceRecord
                {
                    SlNo = (++maxSlNo).ToString(),
                    InvoiceId = invoiceNo,
                    LifecycleStatus = "Active",
                    StockStatus = "New",
                    UnitCost = costPerDevice,
                    AdminCost = invoiceAdminCost,
                    DeviceName = devType,
                    Type = "GPS",
                    Status = "Active",
                    VehicleNo = "",
                    NetworkOperator = (model.NetworkOperator ?? "").Trim(),
                    Notes = model.IsAddon ? $"Add-on device under batch {invoiceNo}" : $"Placeholder from manual invoice {invoiceNo}",
                    CreatedAt = DateTime.UtcNow.ToString("O")
                });
            }

            UpdateLinkedTransactionsProfit(storeData, invoiceNo, costPerDevice);
        });

        TempData["Success"] = model.IsAddon 
            ? $"{model.Quantity} additional device(s) successfully added under existing batch {invoiceNo}." 
            : $"Invoice {invoiceNo} recorded with {model.Quantity} devices.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public async Task<IActionResult> CreateMultiple([Bind(Prefix = "MultipleModel")] MultipleInvoiceFormViewModel model)
    {
        var data = await _store.SnapshotAsync();
        var existingBatches = data.Invoices
            .Select(i => i.InvoiceNo)
            .Concat(data.Devices.Select(d => d.InvoiceId))
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => int.TryParse(b, out var n) ? n : int.MaxValue)
            .ThenBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invoiceNo = model.IsAddon ? model.ExistingInvoiceNo?.Trim() : model.InvoiceNo?.Trim();
        if (string.IsNullOrWhiteSpace(invoiceNo))
        {
            TempData["Error"] = model.IsAddon ? "Please select an existing batch for the add-on." : "Please enter a valid invoice number.";
            return View("Create", new InvoiceCreatePageViewModel { MultipleModel = model, ActiveTab = "multiple", ExistingInvoices = existingBatches });
        }

        if (model.File is null || model.File.Length == 0)
        {
            TempData["Error"] = "Choose a valid Excel (.xlsx) or CSV (.csv) file to upload.";
            ModelState.AddModelError("MultipleModel.File", "Choose a valid Excel (.xlsx) or CSV (.csv) file to upload.");
            return View("Create", new InvoiceCreatePageViewModel { MultipleModel = model, ActiveTab = "multiple", ExistingInvoices = existingBatches });
        }

        try
        {
            List<ParsedInvoiceDeviceRow> parsedRows;
            using (var stream = model.File.OpenReadStream())
            {
                parsedRows = _spreadsheets.ParseImportedInvoice(stream, model.File.FileName);
            }

            if (parsedRows.Count == 0)
            {
                TempData["Error"] = "No rows containing valid iccid and imei columns were found in the file.";
                ModelState.AddModelError("MultipleModel.File", "No rows containing valid iccid and imei columns were found.");
                return View("Create", new InvoiceCreatePageViewModel { MultipleModel = model, ActiveTab = "multiple", ExistingInvoices = existingBatches });
            }

            InvoiceRecord? currentInvoice = null;

            await _store.UpdateAsync(storeData =>
            {
                var existingInvoice = storeData.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, invoiceNo, StringComparison.OrdinalIgnoreCase));
                var pbQty = model.PanicButtonQuantity ?? 0;
                var pbTotal = pbQty * (model.PanicButtonCost ?? 0m);
                var isBharat101 = string.Equals(model.DeviceType, "Bharat101", StringComparison.OrdinalIgnoreCase);
                var effectiveRate = (model.Rate ?? 0m) + (isBharat101 && (model.Rate ?? 0m) < 3000 ? 1000m : 0m);
                var deviceAmount = parsedRows.Count * effectiveRate;
                var additionalAmount = deviceAmount + pbTotal + (model.CourierCharges ?? 0m);
                decimal costPerDevice;

                if (model.IsAddon && existingInvoice != null)
                {
                    existingInvoice.Quantity += parsedRows.Count;
                    if (pbQty > 0)
                    {
                        existingInvoice.PanicButtonQuantity += pbQty;
                        existingInvoice.PanicButtonCost = model.PanicButtonCost ?? existingInvoice.PanicButtonCost;
                        existingInvoice.PanicButtonTotal += pbTotal;
                    }
                    if (additionalAmount > 0)
                    {
                        existingInvoice.Amount += additionalAmount;
                        var newExactTotal = existingInvoice.Amount * 1.18m;
                        existingInvoice.TotalWithTaxes = Math.Round(newExactTotal, MidpointRounding.AwayFromZero);
                        existingInvoice.RoundOff = existingInvoice.TotalWithTaxes - newExactTotal;
                        existingInvoice.CostPerDevice = existingInvoice.Quantity > 0 ? existingInvoice.TotalWithTaxes / existingInvoice.Quantity : 0;
                    }
                    costPerDevice = existingInvoice.CostPerDevice;
                    currentInvoice = existingInvoice;
                }
                else
                {
                    var exactTotal = additionalAmount * 1.18m;
                    var roundedTotal = Math.Round(exactTotal, MidpointRounding.AwayFromZero);
                    var roundOff = roundedTotal - exactTotal;
                    costPerDevice = parsedRows.Count > 0 ? roundedTotal / parsedRows.Count : 0;

                    var invoice = new InvoiceRecord
                    {
                        InvoiceNo = invoiceNo,
                        Date = model.Date.ToString("yyyy-MM-dd"),
                        Quantity = parsedRows.Count,
                        DeviceType = string.IsNullOrWhiteSpace(model.DeviceType) ? "GPS" : model.DeviceType.Trim(),
                        Rate = model.Rate ?? 0m,
                        Amount = additionalAmount,
                        CourierCharges = model.CourierCharges ?? 0m,
                        TotalWithTaxes = roundedTotal,
                        RoundOff = roundOff,
                        CostPerDevice = costPerDevice,
                        Notes = (model.Notes ?? "").Trim(),
                        PanicButtonQuantity = model.PanicButtonQuantity ?? 0,
                        PanicButtonCost = model.PanicButtonCost ?? 0m,
                        PanicButtonTotal = pbTotal,
                        NetworkOperator = (model.NetworkOperator ?? "").Trim(),
                        CreatedAt = DateTime.UtcNow.ToString("O")
                    };
                    storeData.Invoices.Add(invoice);
                    currentInvoice = invoice;
                }

                var maxSlNo = storeData.Devices.Count > 0 ? storeData.Devices.Max(d => int.TryParse(d.SlNo, out var val) ? val : 0) : 0;
                var currentBranch = "DMG";
                var devName = !string.IsNullOrWhiteSpace(model.DeviceType) && model.DeviceType != "None" ? model.DeviceType.Trim() : (currentInvoice?.DeviceType ?? "GPS Tracker");
                var config = storeData.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
                var invoiceAdminCost = (config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc)) ? parsedAc : 0m;

                foreach (var row in parsedRows)
                {
                    var cleanRowImei = (row.Imei ?? "").Trim();
                    var cleanRowIccid = (row.Iccid ?? "").Trim();

                    var existingDevice = storeData.Devices.FirstOrDefault(d =>
                        (!string.IsNullOrWhiteSpace(cleanRowImei) && !string.IsNullOrWhiteSpace(cleanRowIccid))
                            ? (string.Equals((d.Imei ?? "").Trim(), cleanRowImei, StringComparison.OrdinalIgnoreCase) && string.Equals((d.Iccid ?? "").Trim(), cleanRowIccid, StringComparison.OrdinalIgnoreCase))
                            : ((!string.IsNullOrWhiteSpace(cleanRowImei) && string.Equals((d.Imei ?? "").Trim(), cleanRowImei, StringComparison.OrdinalIgnoreCase)) ||
                               (!string.IsNullOrWhiteSpace(cleanRowIccid) && string.Equals((d.Iccid ?? "").Trim(), cleanRowIccid, StringComparison.OrdinalIgnoreCase))));

                    if (existingDevice == null)
                    {
                        var crossBranchDevice = _db.Devices.IgnoreQueryFilters().FirstOrDefault(d =>
                            (!string.IsNullOrWhiteSpace(cleanRowImei) && !string.IsNullOrWhiteSpace(cleanRowIccid))
                                ? (string.Equals((d.Imei ?? "").Trim(), cleanRowImei, StringComparison.OrdinalIgnoreCase) && string.Equals((d.Iccid ?? "").Trim(), cleanRowIccid, StringComparison.OrdinalIgnoreCase))
                                : ((!string.IsNullOrWhiteSpace(cleanRowImei) && string.Equals((d.Imei ?? "").Trim(), cleanRowImei, StringComparison.OrdinalIgnoreCase)) ||
                                   (!string.IsNullOrWhiteSpace(cleanRowIccid) && string.Equals((d.Iccid ?? "").Trim(), cleanRowIccid, StringComparison.OrdinalIgnoreCase))));

                        if (crossBranchDevice != null)
                        {
                            crossBranchDevice.Branch = currentBranch;
                            storeData.Devices.Add(crossBranchDevice);
                            existingDevice = crossBranchDevice;
                        }
                    }

                    if (existingDevice != null)
                    {
                        existingDevice.InvoiceId = invoiceNo;
                        existingDevice.Branch = currentBranch;
                        if (string.IsNullOrWhiteSpace(existingDevice.DeviceName) && !string.IsNullOrWhiteSpace(devName)) existingDevice.DeviceName = devName;
                        if (!string.IsNullOrWhiteSpace(model.NetworkOperator)) existingDevice.NetworkOperator = model.NetworkOperator.Trim();
                        if (!string.IsNullOrWhiteSpace(row.DeviceSerialNo)) existingDevice.DeviceSerialNo = row.DeviceSerialNo;
                        if (!string.IsNullOrWhiteSpace(row.Airtel)) existingDevice.SimAirtel = row.Airtel;
                        if (!string.IsNullOrWhiteSpace(row.Bsnl)) existingDevice.SimBsnl = row.Bsnl;
                        if (existingDevice.AdminCost == 0m && invoiceAdminCost > 0m) existingDevice.AdminCost = invoiceAdminCost;
                        if (string.IsNullOrWhiteSpace(existingDevice.Notes) || existingDevice.Notes == "-")
                            existingDevice.Notes = $"Linked to batch {invoiceNo}";
                    }
                    else
                    {
                        storeData.Devices.Add(new DeviceRecord
                        {
                            SlNo = (++maxSlNo).ToString(),
                            InvoiceId = invoiceNo,
                            Branch = currentBranch,
                            Iccid = row.Iccid,
                            Imei = row.Imei,
                            DeviceSerialNo = row.DeviceSerialNo,
                            SimAirtel = row.Airtel,
                            SimBsnl = row.Bsnl,
                            LifecycleStatus = "Active",
                            StockStatus = model.StockStatus,
                            UnitCost = costPerDevice,
                            AdminCost = invoiceAdminCost,
                            DeviceName = devName,
                            Type = "GPS",
                            Status = "Active",
                            VehicleNo = "",
                            Vendor = "",
                            Owner = "",
                            CellNo = "",
                            ActivationDate = "",
                            LatestExpiryDate = "",
                            NetworkOperator = (model.NetworkOperator ?? "").Trim(),
                            Notes = model.IsAddon ? $"Add-on imported under batch {invoiceNo}" : $"Imported under invoice {invoiceNo}",
                            CreatedAt = DateTime.UtcNow.ToString("O")
                        });
                    }
                }

                UpdateLinkedTransactionsProfit(storeData, invoiceNo, costPerDevice);
            });

            TempData["Success"] = model.IsAddon 
                ? $"{parsedRows.Count} device(s) successfully added into existing batch {invoiceNo}."
                : $"Invoice {invoiceNo} created and {parsedRows.Count} devices imported successfully.";

            var dataSnapshot = await _store.SnapshotAsync();
            var importedDevices = dataSnapshot.Devices.Where(d => string.Equals(d.InvoiceId, invoiceNo, StringComparison.OrdinalIgnoreCase)).OrderBy(d => d.SlNoSortable).ToList();

            var result = new MultipleInvoiceResultViewModel
            {
                Invoice = currentInvoice ?? new InvoiceRecord { InvoiceNo = invoiceNo },
                Devices = importedDevices,
                StockStatus = model.StockStatus
            };
            return View("CreateMultipleSuccess", result);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to process import: {ex.Message}";
            ModelState.AddModelError("MultipleModel.File", $"Failed to process import: {ex.Message}");
            return View("Create", new InvoiceCreatePageViewModel { MultipleModel = model, ActiveTab = "multiple", ExistingInvoices = existingBatches });
        }
    }


    [HttpGet]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public async Task<IActionResult> EditByBatch(string batch)
    {
        if (string.IsNullOrWhiteSpace(batch)) return RedirectToAction(nameof(Index));
        var data = await _store.SnapshotAsync();
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        
        var invoice = data.Invoices.FirstOrDefault(i => 
            string.Equals(i.InvoiceNo, batch, StringComparison.OrdinalIgnoreCase) ||
            (BatchColorHelper.RtoBatchToInvoiceMap.TryGetValue(batch, out var invNo) && string.Equals(i.InvoiceNo, invNo, StringComparison.OrdinalIgnoreCase)) ||
            (BatchColorHelper.RtoInvoiceToBatchMap.TryGetValue(batch, out var bName) && string.Equals(i.InvoiceNo, bName, StringComparison.OrdinalIgnoreCase)));

        if (invoice != null)
        {
            return RedirectToAction(nameof(Edit), new { id = invoice.Id });
        }

        var linkedDevices = data.Devices.Where(d => string.Equals(d.InvoiceId, batch, StringComparison.OrdinalIgnoreCase)).ToList();
        var newInvoice = new InvoiceRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Branch = currentBranch,
            InvoiceNo = batch,
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Quantity = linkedDevices.Count > 0 ? linkedDevices.Count : 1,
            DeviceType = linkedDevices.FirstOrDefault(d => !string.IsNullOrEmpty(d.DeviceName))?.DeviceName ?? (currentBranch == "RTO" ? "Bharat101" : "WeTrack"),
            CreatedAt = DateTime.UtcNow.ToString("O")
        };
        await _store.UpdateAsync(storeData =>
        {
            storeData.Invoices.Add(newInvoice);
        });

        return RedirectToAction(nameof(Edit), new { id = newInvoice.Id });
    }

    [HttpGet]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(string id)
    {
        var data = await _store.SnapshotAsync();
        var invoice = data.Invoices.FirstOrDefault(i => i.Id == id);
        if (invoice is null) return NotFound();

        var linkedDevicesCount = data.Devices.Count(d => string.Equals(d.InvoiceId, invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase));
        ViewBag.LinkedDevicesCount = linkedDevicesCount;

        var model = new InvoiceFormViewModel
        {
            Id = invoice.Id,
            Date = DateValue.Parse(invoice.Date) ?? DateTime.UtcNow.Date,
            InvoiceNo = invoice.InvoiceNo,
            DeviceType = invoice.DeviceType,
            Quantity = invoice.Quantity,
            Rate = invoice.Rate,
            CourierCharges = invoice.CourierCharges,
            PanicButtonQuantity = invoice.PanicButtonQuantity,
            PanicButtonCost = invoice.PanicButtonCost,
            Notes = invoice.Notes,
            NetworkOperator = invoice.NetworkOperator,
            LinkedDevicesCount = linkedDevicesCount
        };

        return View(model);
    }

    [HttpPost]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(InvoiceFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Id)) return BadRequest();

        var dataSnapshot = await _store.SnapshotAsync();
        var existing = dataSnapshot.Invoices.FirstOrDefault(i => i.Id == model.Id);
        if (existing is null) return NotFound();

        var newInvoiceNo = model.InvoiceNo?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(newInvoiceNo))
        {
            ModelState.AddModelError("InvoiceNo", "Please enter a valid invoice number.");
        }
        else if (dataSnapshot.Invoices.Any(i => i.Id != model.Id && string.Equals(i.InvoiceNo, newInvoiceNo, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError("InvoiceNo", "This invoice number is already in use by another invoice.");
        }

        var reqAckNo = Request.HasFormContentType && Request.Form.ContainsKey("AckNo") ? Request.Form["AckNo"].ToString() : "";
        var reqEwayBill = Request.HasFormContentType && Request.Form.ContainsKey("EwayBillNumber") ? Request.Form["EwayBillNumber"].ToString() : "";

        if (!ModelState.IsValid)
        {
            var linkedCount = dataSnapshot.Devices.Count(d => string.Equals(d.InvoiceId, existing.InvoiceNo, StringComparison.OrdinalIgnoreCase));
            model.LinkedDevicesCount = linkedCount;
            ViewBag.LinkedDevicesCount = linkedCount;
            return View(model);
        }

        await _store.UpdateAsync(storeData =>
        {
            var invoice = storeData.Invoices.FirstOrDefault(i => i.Id == model.Id);
            if (invoice is null) return;

            var oldInvoiceNo = invoice.InvoiceNo;
            var oldDeviceType = invoice.DeviceType;
            var oldNetworkOperator = invoice.NetworkOperator;

            // In DMG branch, PanicButtonQuantity is manual entry
            var pbQty = model.PanicButtonQuantity ?? 0;
            var pbTotal = pbQty * (model.PanicButtonCost ?? 0m);
            var isBharat101 = string.Equals(model.DeviceType, "Bharat101", StringComparison.OrdinalIgnoreCase);
            var effectiveRate = (model.Rate ?? 0m) + (isBharat101 && (model.Rate ?? 0m) < 3000 ? 1000m : 0m);
            var deviceAmount = model.Quantity * effectiveRate;
            var totalAmount = deviceAmount + pbTotal + (model.CourierCharges ?? 0m);
            var exactTotal = totalAmount * 1.18m;
            var roundedTotal = Math.Round(exactTotal, MidpointRounding.AwayFromZero);
            var roundOff = roundedTotal - exactTotal;
            var costPerDevice = model.Quantity > 0 ? roundedTotal / model.Quantity : 0;

            invoice.Date = model.Date.ToString("yyyy-MM-dd");
            invoice.InvoiceNo = newInvoiceNo;
            invoice.DeviceType = string.IsNullOrWhiteSpace(model.DeviceType) ? "GPS" : model.DeviceType.Trim();
            invoice.Quantity = model.Quantity;
            invoice.Rate = effectiveRate;
            invoice.CourierCharges = model.CourierCharges ?? 0m;
            invoice.PanicButtonQuantity = pbQty;
            invoice.PanicButtonCost = model.PanicButtonCost ?? 0m;
            invoice.PanicButtonTotal = pbTotal;
            invoice.Amount = deviceAmount;
            invoice.RoundOff = roundOff;
            invoice.TotalWithTaxes = roundedTotal;
            invoice.CostPerDevice = costPerDevice;
            invoice.Notes = (model.Notes ?? "").Trim();
            invoice.NetworkOperator = (model.NetworkOperator ?? "").Trim();

            // If InvoiceNo has changed, cascade update to devices and transactions
            if (!string.Equals(oldInvoiceNo, newInvoiceNo, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var d in storeData.Devices.Where(d => string.Equals(d.InvoiceId, oldInvoiceNo, StringComparison.OrdinalIgnoreCase)))
                {
                    d.InvoiceId = newInvoiceNo;
                }
                foreach (var t in storeData.Transactions.Where(t => string.Equals(t.InvoiceId, oldInvoiceNo, StringComparison.OrdinalIgnoreCase)))
                {
                    t.InvoiceId = newInvoiceNo;
                }
            }

            // Update DeviceName if DeviceType changed and device currently has old device type or default name
            if (!string.Equals(oldDeviceType, invoice.DeviceType, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(invoice.DeviceType) && invoice.DeviceType != "None")
            {
                foreach (var d in storeData.Devices.Where(d => string.Equals(d.InvoiceId, newInvoiceNo, StringComparison.OrdinalIgnoreCase)))
                {
                    if (string.IsNullOrWhiteSpace(d.DeviceName) || d.DeviceName == oldDeviceType || d.DeviceName == "GPS Tracker" || d.DeviceName == "GPS")
                    {
                        d.DeviceName = invoice.DeviceType;
                    }
                }
            }

            // Cascade NetworkOperator to devices if changed
            if (!string.Equals(oldNetworkOperator, invoice.NetworkOperator, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var d in storeData.Devices.Where(d => string.Equals(d.InvoiceId, newInvoiceNo, StringComparison.OrdinalIgnoreCase)))
                {
                    d.NetworkOperator = invoice.NetworkOperator;
                }
            }

            // Recalculate UnitCost on devices and profit on linked transactions
            UpdateLinkedTransactionsProfit(storeData, newInvoiceNo, costPerDevice);
        });

        TempData["Success"] = $"Invoice {newInvoiceNo} successfully updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public async Task<IActionResult> Delete(string id)
    {
        await _store.UpdateAsync(data =>
        {
            var invoice = data.Invoices.FirstOrDefault(i => i.Id == id);
            if (invoice != null)
            {
                // Unlink or delete associated devices
                foreach (var d in data.Devices.Where(d => string.Equals(d.InvoiceId, invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    if (d.StockStatus == "New")
                    {
                        data.Devices.Remove(d);
                    }
                    else
                    {
                        d.InvoiceId = "";
                        d.Notes = d.Notes + " (Invoice deleted)";
                    }
                }

                // Delete the invoice
                data.Invoices.Remove(invoice);
            }
        });
        TempData["Success"] = "Invoice and associated devices deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("~/Invoices/ExportAll")]
    public async Task<IActionResult> ExportAll(
        [FromQuery] string? query = null,
        [FromQuery] string? networkOperator = null,
        [FromQuery] string? fromDate = null,
        [FromQuery] string? toDate = null,
        [FromQuery] int? quantity = null,
        [FromQuery] string? branch = null)
    {
        var data = await _store.SnapshotAsync();
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        var filtered = data.Invoices.OrderByDescending(item => DateValue.Parse(item.Date) ?? DateTime.MinValue).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            filtered = filtered.Where(i => 
                (i.InvoiceNo ?? "").ToLowerInvariant().Contains(q) ||
                (i.DeviceType ?? "").ToLowerInvariant().Contains(q) ||
                (i.Notes ?? "").ToLowerInvariant().Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(networkOperator))
        {
            filtered = filtered.Where(i => 
                string.Equals(i.NetworkOperator?.Trim(), networkOperator.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (quantity.HasValue && quantity.Value > 0)
        {
            filtered = filtered.Where(i => i.Quantity == quantity.Value);
        }

        DateTime? fromDt = DateValue.Parse(fromDate);
        if (fromDt.HasValue)
        {
            filtered = filtered.Where(i => {
                var d = DateValue.Parse(i.Date);
                return d.HasValue && d.Value.Date >= fromDt.Value.Date;
            });
        }

        DateTime? toDt = DateValue.Parse(toDate);
        if (toDt.HasValue)
        {
            filtered = filtered.Where(i => {
                var d = DateValue.Parse(i.Date);
                return d.HasValue && d.Value.Date <= toDt.Value.Date;
            });
        }

        var invoices = filtered.ToList();
        var bytes = _spreadsheets.ExportInvoicesWithDevices(invoices, data.Devices, activeBranch);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"mygts-invoices-{activeBranch.ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("~/Invoices/ExportInvoice/{id}")]
    public async Task<IActionResult> ExportInvoice(string id, [FromQuery] string? branch = null)
    {
        var data = await _store.SnapshotAsync();
        var invoice = data.Invoices.FirstOrDefault(i => i.Id == id);
        if (invoice is null) return NotFound();

        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        var devices = data.Devices.Where(d => 
            string.Equals(d.InvoiceId, invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase) ||
            (BatchColorHelper.RtoInvoiceToBatchMap.TryGetValue(invoice.InvoiceNo, out var bName) && string.Equals(d.InvoiceId, bName, StringComparison.OrdinalIgnoreCase))).ToList();

        var bytes = _spreadsheets.ExportDevices(devices, activeBranch);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"mygts-invoice-{invoice.InvoiceNo}.xlsx");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult DownloadSampleRoster([FromQuery] string? branch = null)
    {
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        var filename = activeBranch == "RTO" ? "sample-invoice-device-roster-rto.xlsx" : "sample-invoice-device-roster-dmg.xlsx";
        var sheetName = activeBranch == "RTO" ? "Device_Roster_RTO" : "Device_Roster_DMG";
        var isRto = activeBranch == "RTO";
        var headers = isRto ? new[] { "iccid", "imei", "serial number" } : new[] { "iccid", "imei", "airtel", "bsnl" };
        var rows = isRto ? new List<string[]>
        {
            new[] { "899102506450567099", "358980102326353", "SN-RTO-1001" },
            new[] { "899102506450567222", "358980102354850", "SN-RTO-1002" },
            new[] { "899102506450567156", "358980102368108", "SN-RTO-1003" },
            new[] { "899102506450567230", "358980102363240", "SN-RTO-1004" }
        } : new List<string[]>
        {
            new[] { "899102506450567099", "358980102326353", "5754219776805", "5752052584890" },
            new[] { "899102506450567222", "358980102354850", "5754219777013", "5752052584903" },
            new[] { "899102506450567156", "358980102368108", "5754219776950", "5752052584896" },
            new[] { "899102506450567230", "358980102363240", "5754219777014", "5752052584904" }
        };

        var bytes = _spreadsheets.CreateFormattedSampleWorkbook(sheetName, headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult DownloadSampleInvoice([FromQuery] string? branch = null)
    {
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        if (activeBranch == "RTO")
        {
            var rtoHeaders = new[] { "iccid", "imei", "serial number" };
            var rtoRows = new List<string[]>
            {
                new[] { "899102506450567099", "358980102326353", "SN-RTO-1001" },
                new[] { "899102506450567222", "358980102354850", "SN-RTO-1002" },
                new[] { "899102506450567156", "358980102368108", "SN-RTO-1003" },
                new[] { "899102506450567230", "358980102363240", "SN-RTO-1004" }
            };
            var rtoBytes = _spreadsheets.CreateFormattedSampleWorkbook("Device_Roster_RTO", rtoHeaders, rtoRows);
            return File(rtoBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "sample-invoice-device-roster-rto.xlsx");
        }

        var filename = "sample-invoices-dmg.xlsx";
        var sheetName = "Device_Roster_DMG";
        var headers = new[] { "iccid", "imei", "airtel", "bsnl" };
        var rows = new List<string[]>
        {
            new[] { "899102506450567099", "358980102326353", "5754219776805", "5752052584890" },
            new[] { "899102506450567222", "358980102354850", "5754219777013", "5752052584903" },
            new[] { "899102506450567156", "358980102368108", "5754219776950", "5752052584896" },
            new[] { "899102506450567230", "358980102363240", "5754219777014", "5752052584904" }
        };

        var bytes = _spreadsheets.CreateFormattedSampleWorkbook(sheetName, headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }

    [HttpGet]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public IActionResult Import()
    {
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        ViewBag.CurrentBranch = currentBranch;
        return View(new ImportViewModel());
    }

    [HttpPost]
    [RequireModulePermission("Invoices", PermissionLevel.Edit)]
    public async Task<IActionResult> Import(ImportViewModel model)
    {
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        ViewBag.CurrentBranch = currentBranch;

        if (model.Workbook is null || model.Workbook.Length == 0)
        {
            ModelState.AddModelError("Workbook", "Choose a valid Excel (.xlsx) file to upload.");
            return View(model);
        }

        try
        {
            ParsedInvoiceWorkbookResult parseResult;
            using (var stream = model.Workbook.OpenReadStream())
            {
                parseResult = _spreadsheets.ParseInvoicesWorkbook(stream, model.Workbook.FileName);
            }

            if (!parseResult.HasInvoicesSheet || parseResult.Packages.Count == 0)
            {
                if (parseResult.FallbackRosterDevices.Count > 0)
                {
                    ModelState.AddModelError("Workbook", "The uploaded file does not contain a main Invoices sheet. To import a device roster for a single batch, use '+ Add invoice' -> 'Multiple (Upload File)'.");
                }
                else
                {
                    ModelState.AddModelError("Workbook", "No valid invoice records were found in the uploaded workbook. Ensure the file contains an 'Invoices' sheet with invoice rows.");
                }
                return View(model);
            }

            int importedInvoicesCount = 0;
            int importedDevicesCount = 0;

            await _store.UpdateAsync(storeData =>
            {
                var maxSlNo = storeData.Devices.Count > 0 ? storeData.Devices.Max(d => int.TryParse(d.SlNo, out var val) ? val : 0) : 0;

                foreach (var package in parseResult.Packages)
                {
                    var invNo = package.Invoice.InvoiceNo?.Trim();
                    if (string.IsNullOrWhiteSpace(invNo)) continue;

                    var sheetDeviceCount = package.FullDevices.Count > 0 ? package.FullDevices.Count : package.Devices.Count;
                    var qty = sheetDeviceCount > 0 ? sheetDeviceCount : (package.Invoice.Quantity > 0 ? package.Invoice.Quantity : 1);

                    var pbQty = package.Invoice.PanicButtonQuantity > 0 ? package.Invoice.PanicButtonQuantity : 0;
                    var pbTotal = pbQty * package.Invoice.PanicButtonCost;
                    var totalAmount = (qty * package.Invoice.Rate) + pbTotal + package.Invoice.CourierCharges;
                    var exactTotal = totalAmount * 1.18m;
                    var roundedTotal = Math.Round(exactTotal, MidpointRounding.AwayFromZero);
                    var roundOff = roundedTotal - exactTotal;
                    var costPerDevice = qty > 0 ? roundedTotal / qty : 0;

                    var existingInv = storeData.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, invNo, StringComparison.OrdinalIgnoreCase));
                    if (existingInv != null)
                    {
                        existingInv.Date = package.Invoice.Date;
                        existingInv.DeviceType = package.Invoice.DeviceType;
                        if (!string.IsNullOrWhiteSpace(package.Invoice.NetworkOperator)) existingInv.NetworkOperator = package.Invoice.NetworkOperator;
                        existingInv.Quantity = qty;
                        existingInv.Rate = package.Invoice.Rate;
                        existingInv.CourierCharges = package.Invoice.CourierCharges;
                        existingInv.PanicButtonQuantity = pbQty;
                        existingInv.PanicButtonCost = package.Invoice.PanicButtonCost;
                        existingInv.PanicButtonTotal = pbTotal;
                        existingInv.Amount = totalAmount;
                        existingInv.RoundOff = roundOff;
                        existingInv.TotalWithTaxes = roundedTotal;
                        existingInv.CostPerDevice = costPerDevice;
                        if (!string.IsNullOrWhiteSpace(package.Invoice.Notes))
                        {
                            existingInv.Notes = package.Invoice.Notes;
                        }
                    }
                    else
                    {
                        var newInv = new InvoiceRecord
                        {
                            InvoiceNo = invNo,
                            Date = package.Invoice.Date,
                            DeviceType = package.Invoice.DeviceType,
                            NetworkOperator = package.Invoice.NetworkOperator ?? "",
                            Quantity = qty,
                            Rate = package.Invoice.Rate,
                            CourierCharges = package.Invoice.CourierCharges,
                            PanicButtonQuantity = pbQty,
                            PanicButtonCost = package.Invoice.PanicButtonCost,
                            PanicButtonTotal = pbTotal,
                            Amount = totalAmount,
                            RoundOff = roundOff,
                            TotalWithTaxes = roundedTotal,
                            CostPerDevice = costPerDevice,
                            Notes = package.Invoice.Notes,
                            CreatedAt = DateTime.UtcNow.ToString("O"),
                            Branch = currentBranch
                        };
                        storeData.Invoices.Add(newInv);
                    }
                    importedInvoicesCount++;

                    var config = storeData.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
                    var invoiceAdminCost = (config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc)) ? parsedAc : 0m;

                    if (package.FullDevices.Count > 0)
                    {
                        foreach (var fd in package.FullDevices)
                        {
                            var cleanFdImei = (fd.Imei ?? "").Trim();
                            var cleanFdIccid = (fd.Iccid ?? "").Trim();

                            var existingDevice = storeData.Devices.FirstOrDefault(d =>
                                (!string.IsNullOrWhiteSpace(cleanFdImei) && !string.IsNullOrWhiteSpace(cleanFdIccid))
                                    ? (string.Equals((d.Imei ?? "").Trim(), cleanFdImei, StringComparison.OrdinalIgnoreCase) && string.Equals((d.Iccid ?? "").Trim(), cleanFdIccid, StringComparison.OrdinalIgnoreCase))
                                    : ((!string.IsNullOrWhiteSpace(cleanFdImei) && string.Equals((d.Imei ?? "").Trim(), cleanFdImei, StringComparison.OrdinalIgnoreCase)) ||
                                       (!string.IsNullOrWhiteSpace(cleanFdIccid) && string.Equals((d.Iccid ?? "").Trim(), cleanFdIccid, StringComparison.OrdinalIgnoreCase))));

                            if (existingDevice == null)
                            {
                                var crossBranchDev = _db.Devices.IgnoreQueryFilters().FirstOrDefault(d =>
                                    (!string.IsNullOrWhiteSpace(cleanFdImei) && !string.IsNullOrWhiteSpace(cleanFdIccid))
                                        ? (string.Equals((d.Imei ?? "").Trim(), cleanFdImei, StringComparison.OrdinalIgnoreCase) && string.Equals((d.Iccid ?? "").Trim(), cleanFdIccid, StringComparison.OrdinalIgnoreCase))
                                        : ((!string.IsNullOrWhiteSpace(cleanFdImei) && string.Equals((d.Imei ?? "").Trim(), cleanFdImei, StringComparison.OrdinalIgnoreCase)) ||
                                           (!string.IsNullOrWhiteSpace(cleanFdIccid) && string.Equals((d.Iccid ?? "").Trim(), cleanFdIccid, StringComparison.OrdinalIgnoreCase))));

                                if (crossBranchDev != null)
                                {
                                    crossBranchDev.Branch = currentBranch;
                                    storeData.Devices.Add(crossBranchDev);
                                    existingDevice = crossBranchDev;
                                }
                            }

                            if (existingDevice != null)
                            {
                                existingDevice.InvoiceId = invNo;
                                existingDevice.Branch = currentBranch;
                                existingDevice.UnitCost = costPerDevice;
                                if (existingDevice.AdminCost == 0m && invoiceAdminCost > 0m) existingDevice.AdminCost = invoiceAdminCost;
                                if (!string.IsNullOrWhiteSpace(package.Invoice.NetworkOperator)) existingDevice.NetworkOperator = package.Invoice.NetworkOperator;
                                if (!string.IsNullOrWhiteSpace(fd.SimAirtel)) existingDevice.SimAirtel = fd.SimAirtel;
                                if (!string.IsNullOrWhiteSpace(fd.SimBsnl)) existingDevice.SimBsnl = fd.SimBsnl;
                                if (!string.IsNullOrWhiteSpace(fd.VehicleNo)) existingDevice.VehicleNo = fd.VehicleNo;
                                if (!string.IsNullOrWhiteSpace(fd.DeviceName)) existingDevice.DeviceName = fd.DeviceName;
                                if (!string.IsNullOrWhiteSpace(fd.DeviceSerialNo)) existingDevice.DeviceSerialNo = fd.DeviceSerialNo;
                            }
                            else
                            {
                                fd.SlNo = (++maxSlNo).ToString();
                                fd.InvoiceId = invNo;
                                fd.UnitCost = costPerDevice;
                                if (fd.AdminCost == 0m) fd.AdminCost = invoiceAdminCost;
                                fd.Branch = currentBranch;
                                if (!string.IsNullOrWhiteSpace(package.Invoice.NetworkOperator)) fd.NetworkOperator = package.Invoice.NetworkOperator;
                                storeData.Devices.Add(fd);
                            }
                            importedDevicesCount++;
                        }
                    }
                    else if (package.Devices.Count > 0)
                    {
                        var devName = !string.IsNullOrWhiteSpace(package.Invoice.DeviceType) && package.Invoice.DeviceType != "None" ? package.Invoice.DeviceType.Trim() : "GPS Tracker";
                        foreach (var row in package.Devices)
                        {
                            var cleanRowImei = (row.Imei ?? "").Trim();
                            var cleanRowIccid = (row.Iccid ?? "").Trim();

                            var existingDevice = storeData.Devices.FirstOrDefault(d =>
                                (!string.IsNullOrWhiteSpace(cleanRowImei) && !string.IsNullOrWhiteSpace(cleanRowIccid))
                                    ? (string.Equals((d.Imei ?? "").Trim(), cleanRowImei, StringComparison.OrdinalIgnoreCase) && string.Equals((d.Iccid ?? "").Trim(), cleanRowIccid, StringComparison.OrdinalIgnoreCase))
                                    : ((!string.IsNullOrWhiteSpace(cleanRowImei) && string.Equals((d.Imei ?? "").Trim(), cleanRowImei, StringComparison.OrdinalIgnoreCase)) ||
                                       (!string.IsNullOrWhiteSpace(cleanRowIccid) && string.Equals((d.Iccid ?? "").Trim(), cleanRowIccid, StringComparison.OrdinalIgnoreCase))));

                            if (existingDevice == null)
                            {
                                var crossBranchDev = _db.Devices.IgnoreQueryFilters().FirstOrDefault(d =>
                                    (!string.IsNullOrWhiteSpace(cleanRowImei) && !string.IsNullOrWhiteSpace(cleanRowIccid))
                                        ? (string.Equals((d.Imei ?? "").Trim(), cleanRowImei, StringComparison.OrdinalIgnoreCase) && string.Equals((d.Iccid ?? "").Trim(), cleanRowIccid, StringComparison.OrdinalIgnoreCase))
                                        : ((!string.IsNullOrWhiteSpace(cleanRowImei) && string.Equals((d.Imei ?? "").Trim(), cleanRowImei, StringComparison.OrdinalIgnoreCase)) ||
                                           (!string.IsNullOrWhiteSpace(cleanRowIccid) && string.Equals((d.Iccid ?? "").Trim(), cleanRowIccid, StringComparison.OrdinalIgnoreCase))));

                                if (crossBranchDev != null)
                                {
                                    crossBranchDev.Branch = currentBranch;
                                    storeData.Devices.Add(crossBranchDev);
                                    existingDevice = crossBranchDev;
                                }
                            }

                            if (existingDevice != null)
                            {
                                existingDevice.InvoiceId = invNo;
                                existingDevice.Branch = currentBranch;
                                existingDevice.UnitCost = costPerDevice;
                                if (existingDevice.AdminCost == 0m && invoiceAdminCost > 0m) existingDevice.AdminCost = invoiceAdminCost;
                                if (!string.IsNullOrWhiteSpace(package.Invoice.NetworkOperator)) existingDevice.NetworkOperator = package.Invoice.NetworkOperator;
                                if (string.IsNullOrWhiteSpace(existingDevice.DeviceName) && !string.IsNullOrWhiteSpace(devName)) existingDevice.DeviceName = devName;
                                if (!string.IsNullOrWhiteSpace(row.DeviceSerialNo)) existingDevice.DeviceSerialNo = row.DeviceSerialNo;
                                if (!string.IsNullOrWhiteSpace(row.Airtel)) existingDevice.SimAirtel = row.Airtel;
                                if (!string.IsNullOrWhiteSpace(row.Bsnl)) existingDevice.SimBsnl = row.Bsnl;
                            }
                            else
                            {
                                storeData.Devices.Add(new DeviceRecord
                                {
                                    SlNo = (++maxSlNo).ToString(),
                                    InvoiceId = invNo,
                                    Branch = currentBranch,
                                    Iccid = row.Iccid,
                                    Imei = row.Imei,
                                    DeviceSerialNo = row.DeviceSerialNo,
                                    SimAirtel = row.Airtel,
                                    SimBsnl = row.Bsnl,
                                    LifecycleStatus = "Active",
                                    StockStatus = "New",
                                    UnitCost = costPerDevice,
                                    AdminCost = invoiceAdminCost,
                                    DeviceName = devName,
                                    Type = "GPS",
                                    Status = "Active",
                                    VehicleNo = "",
                                    Vendor = "",
                                    Owner = "",
                                    CellNo = "",
                                    ActivationDate = "",
                                    LatestExpiryDate = "",
                                    NetworkOperator = package.Invoice.NetworkOperator ?? "",
                                    Notes = $"Imported under invoice {invNo}",
                                    CreatedAt = DateTime.UtcNow.ToString("O")
                                });
                            }
                            importedDevicesCount++;
                        }
                    }

                    UpdateLinkedTransactionsProfit(storeData, invNo, costPerDevice);
                }
            });

            TempData["Success"] = $"Successfully imported {importedInvoicesCount} invoice(s) and {importedDevicesCount} device(s).";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("Workbook", $"Failed to process import: {ex.Message}");
            return View(model);
        }
    }

    private static void UpdateLinkedTransactionsProfit(AppData storeData, string invoiceNo, decimal costPerDevice)
    {
        if (string.IsNullOrWhiteSpace(invoiceNo) || costPerDevice <= 0) return;
        var cleanInv = invoiceNo.Trim();
        var linkedVehicles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in storeData.Devices.Where(d => string.Equals(d.InvoiceId, cleanInv, StringComparison.OrdinalIgnoreCase)))
        {
            d.UnitCost = costPerDevice;
            if (!string.IsNullOrWhiteSpace(d.VehicleNo)) linkedVehicles.Add(d.VehicleNo.Trim());
        }

        foreach (var t in storeData.Transactions.Where(t =>
            t.Amount.HasValue &&
            (string.Equals(t.InvoiceId, cleanInv, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(t.VehicleNo) && linkedVehicles.Contains(t.VehicleNo.Trim())))))
        {
            t.InvoiceId = cleanInv;
            var excludingDmg = t.ExcludingDmg > 0 ? t.ExcludingDmg : (t.Amount ?? 0m);
            t.Profit = excludingDmg - (costPerDevice + t.SimCost + t.CommissionCost + t.AdminCost);
        }
    }
}

