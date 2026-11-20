using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Controllers;

[Authorize]
[RequireModulePermission("Invoices", PermissionLevel.View)]
public sealed class InvoicesController : Controller
{
    private readonly IAppDataStore _store;
    private readonly SpreadsheetService _spreadsheets;

    public InvoicesController(IAppDataStore store, SpreadsheetService spreadsheets)
    {
        _store = store;
        _spreadsheets = spreadsheets;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? query = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var data = await _store.SnapshotAsync();
        var invoices = data.Invoices.OrderByDescending(item => DateValue.Parse(item.Date) ?? DateTime.MinValue).ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            invoices = invoices.Where(i => 
                (i.InvoiceNo ?? "").ToLowerInvariant().Contains(q) ||
                (i.DeviceType ?? "").ToLowerInvariant().Contains(q) ||
                (i.Notes ?? "").ToLowerInvariant().Contains(q)).ToList();
        }

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
            
            var pbTotal = (model.PanicButtonQuantity ?? 0) * (model.PanicButtonCost ?? 0m);
            var additionalAmount = (model.Quantity * (model.Rate ?? 0m)) + pbTotal + (model.CourierCharges ?? 0m);
            
            decimal costPerDevice;
            if (model.IsAddon && existingInvoice != null)
            {
                existingInvoice.Quantity += model.Quantity;
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
                    Rate = model.Rate ?? 0m,
                    Amount = additionalAmount,
                    CourierCharges = model.CourierCharges ?? 0m,
                    TotalWithTaxes = roundedTotal,
                    RoundOff = roundOff,
                    CostPerDevice = costPerDevice,
                    Notes = (model.Notes ?? "").Trim(),
                    PanicButtonCost = model.PanicButtonCost ?? 0m,
                    PanicButtonTotal = pbTotal,
                    NetworkOperator = (model.NetworkOperator ?? "").Trim()
                };
                storeData.Invoices.Add(invoice);
            }

            var maxSlNo = storeData.Devices.Count > 0 ? storeData.Devices.Max(d => int.TryParse(d.SlNo, out var val) ? val : 0) : 0;
            var devType = !string.IsNullOrWhiteSpace(model.DeviceType) && model.DeviceType != "None" ? model.DeviceType.Trim() : (existingInvoice?.DeviceType ?? "GPS Tracker");

            for (int i = 0; i < model.Quantity; i++)
            {
                storeData.Devices.Add(new DeviceRecord
                {
                    SlNo = (++maxSlNo).ToString(),
                    InvoiceId = invoiceNo,
                    LifecycleStatus = "Active",
                    StockStatus = "New",
                    UnitCost = costPerDevice,
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
                var pbTotal = (model.PanicButtonQuantity ?? 0) * (model.PanicButtonCost ?? 0m);
                var additionalAmount = (parsedRows.Count * (model.Rate ?? 0m)) + pbTotal + (model.CourierCharges ?? 0m);
                decimal costPerDevice;

                if (model.IsAddon && existingInvoice != null)
                {
                    existingInvoice.Quantity += parsedRows.Count;
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
                var devName = !string.IsNullOrWhiteSpace(model.DeviceType) && model.DeviceType != "None" ? model.DeviceType.Trim() : (currentInvoice?.DeviceType ?? "GPS Tracker");

                foreach (var row in parsedRows)
                {
                    var existingDevice = storeData.Devices.FirstOrDefault(d => 
                        (!string.IsNullOrWhiteSpace(row.Imei) && string.Equals(d.Imei, row.Imei, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrWhiteSpace(row.Iccid) && string.Equals(d.Iccid, row.Iccid, StringComparison.OrdinalIgnoreCase)));

                    if (existingDevice != null)
                    {
                        existingDevice.InvoiceId = invoiceNo;
                        if (string.IsNullOrWhiteSpace(existingDevice.DeviceName) && !string.IsNullOrWhiteSpace(devName)) existingDevice.DeviceName = devName;
                        if (!string.IsNullOrWhiteSpace(model.NetworkOperator)) existingDevice.NetworkOperator = model.NetworkOperator.Trim();
                        if (!string.IsNullOrWhiteSpace(row.DeviceSerialNo)) existingDevice.DeviceSerialNo = row.DeviceSerialNo;
                        if (!string.IsNullOrWhiteSpace(row.Airtel)) existingDevice.SimAirtel = row.Airtel;
                        if (!string.IsNullOrWhiteSpace(row.Bsnl)) existingDevice.SimBsnl = row.Bsnl;
                        if (string.IsNullOrWhiteSpace(existingDevice.Notes) || existingDevice.Notes == "-")
                            existingDevice.Notes = $"Linked to batch {invoiceNo}";
                    }
                    else
                    {
                        storeData.Devices.Add(new DeviceRecord
                        {
                            SlNo = (++maxSlNo).ToString(),
                            InvoiceId = invoiceNo,
                            Iccid = row.Iccid,
                            Imei = row.Imei,
                            DeviceSerialNo = row.DeviceSerialNo,
                            SimAirtel = row.Airtel,
                            SimBsnl = row.Bsnl,
                            LifecycleStatus = "Active",
                            StockStatus = model.StockStatus,
                            UnitCost = costPerDevice,
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
            PanicButtonQuantity = invoice.Quantity,
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

            // Panic button quantity always matches device quantity
            model.PanicButtonQuantity = model.Quantity;
            var pbTotal = model.Quantity * (model.PanicButtonCost ?? 0m);
            var totalAmount = (model.Quantity * (model.Rate ?? 0m)) + pbTotal + (model.CourierCharges ?? 0m);
            var exactTotal = totalAmount * 1.18m;
            var roundedTotal = Math.Round(exactTotal, MidpointRounding.AwayFromZero);
            var roundOff = roundedTotal - exactTotal;
            var costPerDevice = model.Quantity > 0 ? roundedTotal / model.Quantity : 0;

            invoice.Date = model.Date.ToString("yyyy-MM-dd");
            invoice.InvoiceNo = newInvoiceNo;
            invoice.DeviceType = string.IsNullOrWhiteSpace(model.DeviceType) ? "GPS" : model.DeviceType.Trim();
            invoice.Quantity = model.Quantity;
            invoice.Rate = model.Rate ?? 0m;
            invoice.CourierCharges = model.CourierCharges ?? 0m;
            invoice.PanicButtonQuantity = model.Quantity;
            invoice.PanicButtonCost = model.PanicButtonCost ?? 0m;
            invoice.PanicButtonTotal = pbTotal;
            invoice.Amount = totalAmount;
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
    public async Task<IActionResult> ExportAll([FromQuery] string? query = null, [FromQuery] string? branch = null)
    {
        var data = await _store.SnapshotAsync();
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        var invoices = data.Invoices.OrderByDescending(item => DateValue.Parse(item.Date) ?? DateTime.MinValue).ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim().ToLowerInvariant();
            invoices = invoices.Where(i => 
                (i.InvoiceNo ?? "").ToLowerInvariant().Contains(q) ||
                (i.DeviceType ?? "").ToLowerInvariant().Contains(q) ||
                (i.Notes ?? "").ToLowerInvariant().Contains(q)).ToList();
        }

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

    [HttpGet("~/Invoices/DownloadSampleRoster")]
    [AllowAnonymous]
    public IActionResult DownloadSampleRoster([FromQuery] string? branch = null)
    {
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        var filename = activeBranch == "RTO" ? "sample-invoice-device-roster-rto.xlsx" : "sample-invoice-device-roster-dmg.xlsx";
        var sheetName = activeBranch == "RTO" ? "Device_Roster_RTO" : "Device_Roster_DMG";
        var isRto = activeBranch == "RTO";
        var headers = isRto ? new[] { "iccid", "imei", "device_serial_no", "airtel", "bsnl" } : new[] { "iccid", "imei", "airtel", "bsnl" };
        var rows = isRto ? new List<string[]>
        {
            new[] { "899102506450567099", "358980102326353", "SN-RTO-1001", "5754219776805", "5752052584890" },
            new[] { "899102506450567222", "358980102354850", "SN-RTO-1002", "5754219777013", "5752052584903" },
            new[] { "899102506450567156", "358980102368108", "SN-RTO-1003", "5754219776950", "5752052584896" },
            new[] { "899102506450567230", "358980102363240", "SN-RTO-1004", "5754219777014", "5752052584904" }
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

    [HttpGet("~/Invoices/DownloadSampleInvoice")]
    [AllowAnonymous]
    public IActionResult DownloadSampleInvoice([FromQuery] string? branch = null)
    {
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        var filename = activeBranch == "RTO" ? "sample-invoices-rto.xlsx" : "sample-invoices-dmg.xlsx";
        var mainSheetName = activeBranch == "RTO" ? "Invoices_RTO" : "Invoices_DMG";
        var inv1 = activeBranch == "RTO" ? "INV-RTO-2026-001" : "INV-DMG-2026-001";
        var inv2 = activeBranch == "RTO" ? "INV-RTO-2026-002" : "INV-DMG-2026-002";

        var rosterHeaders = activeBranch == "RTO"
            ? new[] { "iccid", "imei", "device_serial_no", "airtel", "bsnl" }
            : new[] { "iccid", "imei", "airtel", "bsnl" };
        var inv1Rows = activeBranch == "RTO"
            ? new List<string[]>
            {
                new[] { "899102506450567099", "358980102326353", "SN-RTO-1001", "5754219776805", "5752052584890" },
                new[] { "899102506450567222", "358980102354850", "SN-RTO-1002", "5754219777013", "5752052584903" }
            }
            : new List<string[]>
            {
                new[] { "899102506450567099", "358980102326353", "5754219776805", "5752052584890" },
                new[] { "899102506450567222", "358980102354850", "5754219777013", "5752052584903" }
            };
        var inv2Rows = activeBranch == "RTO"
            ? new List<string[]>
            {
                new[] { "899102506450567156", "358980102368108", "SN-RTO-1003", "5754219776950", "5752052584896" },
                new[] { "899102506450567230", "358980102363240", "SN-RTO-1004", "5754219777014", "5752052584904" }
            }
            : new List<string[]>
            {
                new[] { "899102506450567156", "358980102368108", "5754219776950", "5752052584896" },
                new[] { "899102506450567230", "358980102363240", "5754219777014", "5752052584904" }
            };

        var sheets = new List<MultiSheetItem>
        {
            new MultiSheetItem
            {
                SheetName = mainSheetName,
                Headers = SpreadsheetService.InvoiceHeadersClean,
                Rows = new List<string[]>
                {
                    new[] { "2026-09-01", "Bharat101", inv1, "2", "2500.00", "150.00", "2", "250.00", "500.00", "5650.00", "0.70", "6667.00", "3333.50", "Sample batch 1" },
                    new[] { "2026-09-02", "Bharat101", inv2, "2", "2600.00", "150.00", "2", "250.00", "500.00", "5850.00", "0.70", "6903.00", "3451.50", "Sample batch 2" }
                }
            },
            new MultiSheetItem
            {
                SheetName = inv1,
                Headers = rosterHeaders,
                Rows = inv1Rows
            },
            new MultiSheetItem
            {
                SheetName = inv2,
                Headers = rosterHeaders,
                Rows = inv2Rows
            }
        };

        var bytes = _spreadsheets.CreateMultiSheetWorkbook(sheets);
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

                    var pbTotal = qty * package.Invoice.PanicButtonCost;
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
                        existingInv.Quantity = qty;
                        existingInv.Rate = package.Invoice.Rate;
                        existingInv.CourierCharges = package.Invoice.CourierCharges;
                        existingInv.PanicButtonQuantity = qty;
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
                            Quantity = qty,
                            Rate = package.Invoice.Rate,
                            CourierCharges = package.Invoice.CourierCharges,
                            PanicButtonQuantity = qty,
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

                    if (package.FullDevices.Count > 0)
                    {
                        foreach (var fd in package.FullDevices)
                        {
                            var existingDevice = storeData.Devices.FirstOrDefault(d =>
                                (!string.IsNullOrWhiteSpace(fd.Imei) && string.Equals(d.Imei, fd.Imei, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(fd.Iccid) && string.Equals(d.Iccid, fd.Iccid, StringComparison.OrdinalIgnoreCase)));

                            if (existingDevice != null)
                            {
                                existingDevice.InvoiceId = invNo;
                                existingDevice.UnitCost = costPerDevice;
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
                                fd.Branch = currentBranch;
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
                            var existingDevice = storeData.Devices.FirstOrDefault(d =>
                                (!string.IsNullOrWhiteSpace(row.Imei) && string.Equals(d.Imei, row.Imei, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrWhiteSpace(row.Iccid) && string.Equals(d.Iccid, row.Iccid, StringComparison.OrdinalIgnoreCase)));

                            if (existingDevice != null)
                            {
                                existingDevice.InvoiceId = invNo;
                                existingDevice.UnitCost = costPerDevice;
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
                                    Iccid = row.Iccid,
                                    Imei = row.Imei,
                                    DeviceSerialNo = row.DeviceSerialNo,
                                    SimAirtel = row.Airtel,
                                    SimBsnl = row.Bsnl,
                                    LifecycleStatus = "Active",
                                    StockStatus = "New",
                                    UnitCost = costPerDevice,
                                    DeviceName = devName,
                                    Type = "GPS",
                                    Status = "Active",
                                    VehicleNo = "",
                                    Vendor = "",
                                    Owner = "",
                                    CellNo = "",
                                    ActivationDate = "",
                                    LatestExpiryDate = "",
                                    Notes = $"Imported under invoice {invNo}",
                                    CreatedAt = DateTime.UtcNow.ToString("O"),
                                    Branch = currentBranch
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
            t.Profit = excludingDmg - (costPerDevice + t.SimCost + t.CommissionCost);
        }
    }
}

