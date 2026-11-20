using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;
using System.IO;

namespace MygtsMvc.Controllers;

[Authorize]
[RequireModulePermission("Transactions", PermissionLevel.View)]
public sealed class TransactionsController(IAppDataStore _store, SpreadsheetService _spreadsheets, MygtsMvc.Data.ApplicationDbContext _db) : Controller
{
    [HttpGet("Dump")]
    [AllowAnonymous]
    public async Task<IActionResult> Dump()
    {
        var data = await _store.SnapshotAsync();
        return Json(data.Transactions.Take(5));
    }

    private IEnumerable<TransactionRecord> FilterAndSortTransactions(AppData data, TransactionFilterViewModel filters)
    {
        var allTxns = data.Transactions.AsEnumerable().Reverse().ToList();
        var transactions = allTxns.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filters.Query))
        {
            var terms = filters.Query.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries);
            transactions = transactions.Where(item =>
            {
                var combined = string.Join(" ",
                    item.VehicleNo, item.Vendor, item.Utr, item.BatchId,
                    item.Date, DateValue.Short(item.Date), DateValue.Parse(item.Date)?.ToString("MMM yy"), DateValue.Parse(item.Date)?.ToString("MMM dd"),
                    item.InvoiceId, item.PaymentMode, item.PaymentFor, item.PaidTo,
                    item.Extras, item.ActivationDate, item.NewExpiryDate, item.Notes,
                    DateValue.Short(item.ActivationDate), DateValue.Short(item.NewExpiryDate), DateValue.Short(item.CreatedAt));
                return terms.All(term =>
                {
                    bool match = combined.Contains(term, StringComparison.OrdinalIgnoreCase);
                    if (!match && term.Equals("Recharge", StringComparison.OrdinalIgnoreCase)) return combined.Contains("Reactivation", StringComparison.OrdinalIgnoreCase);
                    if (!match && term.Equals("Reactivation", StringComparison.OrdinalIgnoreCase)) return combined.Contains("Recharge", StringComparison.OrdinalIgnoreCase);
                    return match;
                });
            });
        }
        if (!string.IsNullOrWhiteSpace(filters.PaymentMode)) transactions = transactions.Where(item => string.Equals(item.PaymentMode, filters.PaymentMode, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filters.FilterVendor)) transactions = transactions.Where(item => string.Equals(item.Vendor, filters.FilterVendor, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filters.PaymentFor)) transactions = transactions.Where(item => string.Equals(item.PaymentFor, filters.PaymentFor, StringComparison.OrdinalIgnoreCase));

        return filters.SortColumn?.ToLowerInvariant() switch
        {
            "date" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => DateValue.Parse(t.Date)) : transactions.OrderBy(t => DateValue.Parse(t.Date)),
            "vehicle" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.VehicleNo) : transactions.OrderBy(t => t.VehicleNo),
            "payment" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.PaymentFor) : transactions.OrderBy(t => t.PaymentFor),
            "vendor" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.Vendor) : transactions.OrderBy(t => t.Vendor),
            "mode" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.PaymentMode) : transactions.OrderBy(t => t.PaymentMode),
            "batch" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.BatchId) : transactions.OrderBy(t => t.BatchId),
            "amount" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.Amount) : transactions.OrderBy(t => t.Amount),
            "profit" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.Profit) : transactions.OrderBy(t => t.Profit),
            "slno" => filters.SortDirection == "desc" 
                ? transactions.OrderByDescending(t => System.Text.RegularExpressions.Regex.Replace(t.SlNo ?? "", @"\d+", m => m.Value.PadLeft(10, '0'))) 
                : transactions.OrderBy(t => System.Text.RegularExpressions.Regex.Replace(t.SlNo ?? "", @"\d+", m => m.Value.PadLeft(10, '0'))),
            _ => transactions.OrderByDescending(item => item.CreatedAt)
        };
    }

    [HttpGet]
    public async Task<IActionResult> Index(TransactionFilterViewModel filters)
    {
        var data = await _store.SnapshotAsync();
        var allTxns = data.Transactions.AsEnumerable().Reverse().ToList();

        var vendorOptions = allTxns
            .Where(t => !string.IsNullOrWhiteSpace(t.Vendor))
            .Select(t => t.Vendor)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .Select(v => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = v,
                Text = v
            }).ToList();

        var sortedTransactions = FilterAndSortTransactions(data, filters);
        var sortedTransactionsList = sortedTransactions.ToList();
        var totalItems = sortedTransactionsList.Count;
        var totalFilteredAmount = sortedTransactionsList.Sum(item => item.Amount ?? 0m);
        var totalFilteredProfit = sortedTransactionsList.Sum(item => item.Profit ?? 0m);

        var pagedTransactions = (filters.PageSize >= 1000000 || filters.PageSize <= 0)
            ? sortedTransactionsList
            : sortedTransactionsList.Skip((filters.Page - 1) * filters.PageSize).Take(filters.PageSize).ToList();

        return View(new TransactionFilterViewModel
        {
            Query = filters.Query,
            PaymentMode = filters.PaymentMode,
            FilterVendor = filters.FilterVendor,
            VendorOptions = vendorOptions,
            SortColumn = filters.SortColumn,
            SortDirection = filters.SortDirection,
            Page = filters.Page,
            PageSize = filters.PageSize,
            TotalItems = totalItems,
            TotalFilteredAmount = totalFilteredAmount,
            TotalFilteredProfit = totalFilteredProfit,
            Transactions = pagedTransactions,
            PaymentModeOptions = allTxns.Select(t => t.PaymentMode).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
            PaymentForOptions = allTxns.Select(t => t.PaymentFor).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList()
        });
    }

    [HttpGet]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> Create(string? vehicleNo = null)
    {
        var data = await _store.SnapshotAsync();
        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";
        ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";
        ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";
        ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";
        ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "12";
        ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "0";

        var model = new TransactionFormViewModel();
        if (!string.IsNullOrWhiteSpace(vehicleNo))
        {
            vehicleNo = DeviceLifecycleHelper.ParseVehicleSlash(vehicleNo).VehicleNo;
            model.VehicleNo = vehicleNo.Trim();
            var dev = data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo?.Replace(" ", ""), vehicleNo.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
            if (dev != null)
            {
                model.Vendor = dev.Vendor ?? "";
                model.InvoiceId = dev.InvoiceId ?? "";
                model.ActivationDate = DateValue.Parse(dev.ActivationDate)?.ToString("yyyy-MM-dd") ?? dev.ActivationDate;
                var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
                var calculatedExpiry = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate, currentBranch, dev.Type, config);
                model.NewExpiryDate = calculatedExpiry ?? DateValue.Parse(dev.LatestExpiryDate)?.ToString("yyyy-MM-dd") ?? dev.LatestExpiryDate;
                model.PaymentFor = "None";
            }
        }
        return View(model);
    }

    [HttpPost]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> Create(TransactionFormViewModel model)
    {
        if (model.CurrentMode == EntryMode.MergePartPayments)
        {
            ModelState.Remove(nameof(model.Amount));
            if (model.MergePayments != null && model.MergePayments.Count > 0)
            {
                model.Amount = model.MergePayments.Sum(p => p.Amount);
            }
        }
        else if (model.CurrentMode == EntryMode.BulkAllocation)
        {
            ModelState.Remove(nameof(model.Amount));
            ModelState.Remove(nameof(model.VehicleNo));
        }
        else if (model.CurrentMode == EntryMode.Standard)
        {
            var snap = await _store.SnapshotAsync();
            var normalized = model.VehicleNo?.Replace(" ", "").Trim();
            var deviceExists = snap.Devices.Any(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), normalized, StringComparison.OrdinalIgnoreCase));
            if (!deviceExists) ModelState.AddModelError("VehicleNo", "Not available in the data");
        }

        if (!ModelState.IsValid)
        {
            var data = await _store.SnapshotAsync();
            var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
            ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";
            ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";
            ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";
            ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";
            return View(model);
        }
        await _store.UpdateAsync(data =>
        {
            if (model.CurrentMode == EntryMode.MergePartPayments)
            {
                var validPayments = (model.MergePayments ?? []).Where(p => p.Amount > 0 || !string.IsNullOrWhiteSpace(p.Utr)).ToList();
                if (validPayments.Count == 0) { ModelState.AddModelError("MergePayments", "Enter at least one payment."); return; }

                var totalAmount = validPayments.Sum(p => p.Amount);
                var batchId = string.IsNullOrWhiteSpace(model.BatchId) ? $"M-{Guid.NewGuid().ToString("N").Substring(0, 8)}" : model.BatchId.Trim();
                var config = data.Config.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
                var device = data.Devices.FirstOrDefault(item => string.Equals(item.VehicleNo, (model.VehicleNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                var deviceInvoice = device?.InvoiceId ?? "";
                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, deviceInvoice, StringComparison.OrdinalIgnoreCase));

                var masterDeviceCost = 0m;
                if (model.PaymentFor == "Activation" || model.PaymentFor == "NEW DEVICE" || model.PaymentFor == "Device")
                    masterDeviceCost = invoice?.CostPerDevice ?? device?.UnitCost ?? 0m;
                else if (model.PaymentFor == "Recharge" && config.TryGetValue("PRICE_RECHARGE", out var rPrice) && decimal.TryParse(rPrice, out var configuredRPrice))
                    masterDeviceCost = configuredRPrice;
                else if (model.PaymentFor == "Reactivation" && config.TryGetValue("PRICE_REACTIVATION", out var reaPrice) && decimal.TryParse(reaPrice, out var configuredReaPrice))
                    masterDeviceCost = configuredReaPrice;

                var simCost = 0m;
                var commissionCost = 0m;
                var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
                var simKey = (currentBranch == "RTO" && model.Type == "Old") ? "SIM_COST_1_YEAR" : "SIM_COST";
                if (config.TryGetValue(simKey, out var sCost) && decimal.TryParse(sCost, out var parsedSCost)) simCost = parsedSCost;
                if (config.TryGetValue("COMMISSION_COST", out var cCost) && decimal.TryParse(cCost, out var parsedCCost)) commissionCost = parsedCCost;

                var combinedModes = string.Join(" + ", validPayments.Select(p => p.PaymentMode.Trim()).Where(m => !string.IsNullOrEmpty(m)).Distinct());
                var combinedUtrs = string.Join(" + ", validPayments.Select(p => p.Utr?.Trim()).Where(u => !string.IsNullOrEmpty(u)));
                var combinedPaidTo = string.Join(" / ", validPayments.Select(p => p.PaidTo?.Trim()).Where(pt => !string.IsNullOrEmpty(pt)).Distinct());
                var partsSummary = string.Join(" + ", validPayments.Select(p => $"{p.Amount:F0} ({p.PaymentMode.Trim()})"));
                var notes = string.IsNullOrWhiteSpace(model.Notes) ? $"Split Payment: {partsSummary}" : $"{model.Notes.Trim()} (Split: {partsSummary})";

                var excludingDmg = totalAmount;
                var profit = excludingDmg - (masterDeviceCost + simCost + commissionCost);

                if (string.IsNullOrWhiteSpace(model.NewExpiryDate) && !string.IsNullOrWhiteSpace(model.ActivationDate))
                {
                    model.NewExpiryDate = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate, currentBranch, model.Type, config) ?? "";
                }

                var transaction = new TransactionRecord
                {
                    Date = model.Date.ToString("yyyy-MM-dd"),
                    SlNo = device?.SlNo ?? "",
                    VehicleNo = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim(),
                    Vendor = (model.Vendor ?? "").Trim(),
                    InvoiceId = deviceInvoice,
                    Type = model.Type ?? "New",
                    PaymentMode = string.IsNullOrWhiteSpace(combinedModes) ? "Merged" : combinedModes,
                    PaymentFor = model.PaymentFor ?? (currentBranch == "RTO" ? "NEW DEVICE" : "Activation"),
                    Amount = totalAmount,
                    BatchId = batchId,
                    Utr = combinedUtrs,
                    PaidTo = combinedPaidTo,
                    ActivationDate = model.ActivationDate?.Trim() ?? "",
                    NewExpiryDate = model.NewExpiryDate?.Trim() ?? "",
                    Notes = notes,
                    ExcludingDmg = excludingDmg,
                    SimCost = simCost,
                    CommissionCost = commissionCost,
                    Profit = profit
                };
                data.Transactions.Add(transaction);

                var deviceToUpdate = data.Devices.FirstOrDefault(item => string.Equals(item.VehicleNo, (model.VehicleNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                if (deviceToUpdate is not null)
                {
                    if (!string.IsNullOrWhiteSpace(model.NewExpiryDate)) deviceToUpdate.LatestExpiryDate = model.NewExpiryDate.Trim();
                    if (!string.IsNullOrWhiteSpace(model.ActivationDate)) deviceToUpdate.ActivationDate = model.ActivationDate.Trim();
                }

                TempData["Success"] = "Merged transaction recorded.";
            }
            else if (model.CurrentMode == EntryMode.BulkAllocation)
            {
                var validAllocations = model.BulkAllocations.Where(a => !string.IsNullOrWhiteSpace(a.VehicleNo) && a.Amount > 0).ToList();
                if (validAllocations.Count == 0) { ModelState.AddModelError("BulkAllocations", "Enter at least one valid vehicle allocation."); return; }

                var config = data.Config.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
                var simCost = 0m;
                var commissionCost = 0m;
                var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
                var simKey = (currentBranch == "RTO" && model.Type == "Old") ? "SIM_COST_1_YEAR" : "SIM_COST";
                if (config.TryGetValue(simKey, out var sCost) && decimal.TryParse(sCost, out var parsedSCost)) simCost = parsedSCost;
                if (config.TryGetValue("COMMISSION_COST", out var cCost) && decimal.TryParse(cCost, out var parsedCCost)) commissionCost = parsedCCost;

                var batchId = model.BatchId?.Trim() ?? "";
                var masterPaymentFor = !string.IsNullOrWhiteSpace(model.PaymentFor) && !model.PaymentFor.Equals("Bulk allocation", StringComparison.OrdinalIgnoreCase) && !model.PaymentFor.Equals("None", StringComparison.OrdinalIgnoreCase)
                    ? model.PaymentFor.Trim()
                    : (currentBranch == "RTO" ? "NEW DEVICE" : "Activation");

                var bulkPartsList = new List<object>();
                decimal totalProfit = 0m;
                decimal totalExcludingDmg = 0m;
                decimal totalSim = 0m;
                decimal totalComm = 0m;

                foreach (var allocation in validAllocations)
                {
                    var cleanVeh = allocation.VehicleNo!.Trim();
                    var device = data.Devices.FirstOrDefault(item => string.Equals(item.VehicleNo, cleanVeh, StringComparison.OrdinalIgnoreCase));
                    var deviceInvoice = device?.InvoiceId ?? "";

                    var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, deviceInvoice, StringComparison.OrdinalIgnoreCase));
                    var deviceCost = (masterPaymentFor == "Activation" || masterPaymentFor == "NEW DEVICE") && device != null ? (invoice?.CostPerDevice ?? device.UnitCost) : 0m;

                    var excludingDmg = allocation.Amount - allocation.ExtrasAmount;
                    if (string.IsNullOrWhiteSpace(allocation.NewExpiryDate) && !string.IsNullOrWhiteSpace(allocation.ActivationDate))
                    {
                        allocation.NewExpiryDate = DeviceLifecycleHelper.CalculateExpiryFromActivation(allocation.ActivationDate, currentBranch, model.Type, config) ?? "";
                    }

                    var aProfit = excludingDmg - (deviceCost + simCost + commissionCost);
                    totalProfit += aProfit;
                    totalExcludingDmg += excludingDmg;
                    totalSim += simCost;
                    totalComm += commissionCost;

                    bulkPartsList.Add(new
                    {
                        vehicleNo = cleanVeh,
                        imei = !string.IsNullOrWhiteSpace(allocation.Imei) ? allocation.Imei.Trim() : (device?.Imei ?? ""),
                        @operator = !string.IsNullOrWhiteSpace(allocation.NetworkOperator) ? allocation.NetworkOperator.Trim() : (device?.NetworkOperator ?? ""),
                        amount = allocation.Amount,
                        profit = aProfit,
                        activationDate = allocation.ActivationDate?.Trim() ?? "",
                        newExpiryDate = allocation.NewExpiryDate?.Trim() ?? ""
                    });

                    if (device is not null)
                    {
                        device.Paid = "Yes";
                        if (!string.IsNullOrWhiteSpace(allocation.ActivationDate)) device.ActivationDate = allocation.ActivationDate.Trim();
                        if (!string.IsNullOrWhiteSpace(allocation.NewExpiryDate)) device.LatestExpiryDate = allocation.NewExpiryDate.Trim();
                    }
                }

                var jsonParts = System.Text.Json.JsonSerializer.Serialize(bulkPartsList);
                var userNotes = model.Notes?.Trim() ?? "";
                var combinedNotes = string.IsNullOrWhiteSpace(userNotes)
                    ? $"Bulk Allocation ({validAllocations.Count} Vehicles): {string.Join(", ", validAllocations.Select(a => a.VehicleNo!.Trim()))} [BULK_PARTS:{jsonParts}]"
                    : $"{userNotes} [BULK_PARTS:{jsonParts}]";

                var totalAmt = model.TotalPayment ?? validAllocations.Sum(a => a.Amount);
                var firstDev = data.Devices.FirstOrDefault(item => string.Equals(item.VehicleNo, validAllocations[0].VehicleNo!.Trim(), StringComparison.OrdinalIgnoreCase));

                var masterTxn = new TransactionRecord
                {
                    Date = model.Date.ToString("yyyy-MM-dd"),
                    VehicleNo = string.Join(", ", validAllocations.Select(a => a.VehicleNo!.Trim())),
                    Imei = firstDev?.Imei ?? "",
                    Vendor = model.Vendor?.Trim() ?? (firstDev?.Vendor ?? ""),
                    InvoiceId = firstDev?.InvoiceId ?? "",
                    PaymentMode = model.PaymentMode,
                    PaymentFor = masterPaymentFor,
                    Amount = totalAmt,
                    BatchId = batchId,
                    Utr = model.Utr?.Trim() ?? "",
                    PaidTo = model.PaidTo?.Trim() ?? "",
                    Notes = combinedNotes,
                    Extras = model.Extras?.Trim() ?? "None",
                    ExcludingDmg = totalExcludingDmg,
                    SimCost = totalSim,
                    CommissionCost = totalComm,
                    Profit = totalProfit,
                    Branch = currentBranch
                };
                data.Transactions.Add(masterTxn);

                TempData["Success"] = $"Allocated {validAllocations.Sum(a => a.Amount):C} across {validAllocations.Count} vehicles.";
            }
            else
            {
                var amount = model.Amount ?? 0m;
                var config = data.Config.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

                var device = data.Devices.FirstOrDefault(item => string.Equals(item.VehicleNo, (model.VehicleNo ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
                var deviceInvoice = model.InvoiceId?.Trim() ?? device?.InvoiceId ?? "";
                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, deviceInvoice, StringComparison.OrdinalIgnoreCase));

                var deviceCost = 0m;
                if (model.PaymentFor == "Activation" || model.PaymentFor == "NEW DEVICE" || model.PaymentFor == "Device") deviceCost = invoice?.CostPerDevice ?? device?.UnitCost ?? 0m;
                else if (model.PaymentFor == "Reactivation" && config.TryGetValue("PRICE_REACTIVATION", out var reaPrice) && decimal.TryParse(reaPrice, out var configuredReaPrice)) deviceCost = configuredReaPrice;

                var simCost = 0m;
                var commissionCost = 0m;
                var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
                var simKey = (currentBranch == "RTO" && model.Type == "Old") ? "SIM_COST_1_YEAR" : "SIM_COST";
                if (config.TryGetValue(simKey, out var sCost) && decimal.TryParse(sCost, out var parsedSCost)) simCost = parsedSCost;
                if (config.TryGetValue("COMMISSION_COST", out var cCost) && decimal.TryParse(cCost, out var parsedCCost)) commissionCost = parsedCCost;

                var excludingDmg = amount - (model.ExtrasAmount ?? 0m);
                var profit = excludingDmg - (deviceCost + simCost + commissionCost);

                if (string.IsNullOrWhiteSpace(model.NewExpiryDate) && !string.IsNullOrWhiteSpace(model.ActivationDate))
                {
                    model.NewExpiryDate = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate, currentBranch, model.Type, config) ?? "";
                }

                var transaction = new TransactionRecord
                {
                    Date = model.Date.ToString("yyyy-MM-dd"),
                    Type = model.Type,
                    VehicleNo = (model.VehicleNo ?? "").Trim(),
                    Vendor = model.Vendor?.Trim() ?? "",
                    InvoiceId = model.InvoiceId?.Trim() ?? "",
                    PaymentMode = model.PaymentMode,
                    PaymentFor = model.PaymentFor,
                    Amount = amount,
                    BatchId = model.BatchId?.Trim() ?? "",
                    Utr = model.Utr?.Trim() ?? "",
                    PaidTo = model.PaidTo?.Trim() ?? "",
                    Extras = model.Extras?.Trim() ?? "",
                    ActivationDate = model.ActivationDate?.Trim() ?? "",
                    NewExpiryDate = model.NewExpiryDate?.Trim() ?? "",
                    Notes = model.Notes?.Trim() ?? "",
                    ExcludingDmg = excludingDmg,
                    SimCost = simCost,
                    CommissionCost = commissionCost,
                    Profit = profit
                };
                data.Transactions.Add(transaction);

                if (device is not null)
                {
                    if (!string.IsNullOrWhiteSpace(transaction.NewExpiryDate)) device.LatestExpiryDate = transaction.NewExpiryDate;
                    if (!string.IsNullOrWhiteSpace(model.ActivationDate)) device.ActivationDate = model.ActivationDate.Trim();
                }
                TempData["Success"] = "Transaction recorded.";
            }
        });
        if (!ModelState.IsValid) return View(model);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> Delete(string id)
    {
        await _store.UpdateAsync(data => data.Transactions.RemoveAll(item => item.Id == id || (item.BatchId == id && item.BatchId.StartsWith("M-"))));
        TempData["Success"] = "Transaction(s) deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(string id)
    {
        var data = await _store.SnapshotAsync();
        var item = data.Transactions.FirstOrDefault(t => t.Id == id);
        if (item is null) return NotFound();

        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";
        ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";
        ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";
        ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";
        ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "12";
        ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "0";

        var itemAmount = item.Amount ?? 0m;
        var itemProfit = item.Profit ?? 0m;
        var excludingDmg = item.ExcludingDmg.HasValue && item.ExcludingDmg.Value > 0 ? item.ExcludingDmg.Value : itemAmount;
        var totalCost = excludingDmg - itemProfit;
        if (totalCost < 0) totalCost = 0;
        var savedDeviceCost = totalCost - item.SimCost - item.CommissionCost;
        if (savedDeviceCost < 0) savedDeviceCost = 0;

        if (savedDeviceCost == 0 && !string.IsNullOrWhiteSpace(item.VehicleNo))
        {
            var device = data.Devices.FirstOrDefault(dev => string.Equals(dev.VehicleNo, item.VehicleNo, StringComparison.OrdinalIgnoreCase));
            if (device != null && !string.IsNullOrWhiteSpace(device.InvoiceId))
            {
                var inv = data.Invoices.FirstOrDefault(invRecord => string.Equals(invRecord.InvoiceNo, device.InvoiceId, StringComparison.OrdinalIgnoreCase));
                if (inv != null && inv.CostPerDevice > 0) savedDeviceCost = inv.CostPerDevice;
            }
        }

        ViewBag.SavedAmount = item.Amount;
        ViewBag.SavedCost = totalCost;
        ViewBag.SavedProfit = item.Profit;
        ViewBag.SavedDeviceCost = savedDeviceCost;

        return View(new TransactionFormViewModel
        {
            Id = item.Id,
            Date = DateValue.Parse(item.Date) ?? DateTime.UtcNow.Date,
            Type = item.Type,
            VehicleNo = item.VehicleNo,
            Vendor = item.Vendor,
            InvoiceId = item.InvoiceId,
            PaymentMode = item.PaymentMode,
            PaymentFor = item.PaymentFor,
            Amount = item.Amount,
            BatchId = item.BatchId,
            Utr = item.Utr,
            PaidTo = item.PaidTo,
            Extras = item.Extras,
            ExtrasAmount = item.Amount.HasValue && item.ExcludingDmg.HasValue ? item.Amount.Value - item.ExcludingDmg.Value : 0m,
            ActivationDate = item.ActivationDate,
            NewExpiryDate = item.NewExpiryDate,
            Notes = item.Notes
        });
    }

    [HttpPost]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(TransactionFormViewModel model)
    {
        var snap = await _store.SnapshotAsync();
        model.VehicleNo = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim();
        var normalized = model.VehicleNo?.Replace(" ", "").Trim();
        var deviceExists = snap.Devices.Any(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        if (!deviceExists) ModelState.AddModelError("VehicleNo", "Not available in the data");

        if (!ModelState.IsValid)
        {
            var snapConfig = await _store.SnapshotAsync();
            var config = snapConfig.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
            ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";
            ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";
            ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";
            ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";
            return View(model);
        }
        await _store.UpdateAsync(data =>
        {
            var item = data.Transactions.FirstOrDefault(t => t.Id == model.Id);
            if (item is null) return;
            var amount = model.Amount ?? 0m;
            var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
            var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
            if (string.IsNullOrWhiteSpace(model.NewExpiryDate) && !string.IsNullOrWhiteSpace(model.ActivationDate))
            {
                model.NewExpiryDate = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate, currentBranch, model.Type, config) ?? "";
            }
            item.Date = model.Date.ToString("yyyy-MM-dd");
            item.VehicleNo = (model.VehicleNo ?? "").Trim();
            item.Vendor = model.Vendor?.Trim() ?? "";
            item.InvoiceId = model.InvoiceId?.Trim() ?? "";
            item.PaymentMode = model.PaymentMode;
            item.PaymentFor = model.PaymentFor;
            item.Amount = amount;

            item.BatchId = model.BatchId?.Trim() ?? "";
            item.Utr = model.Utr?.Trim() ?? "";
            item.PaidTo = model.PaidTo?.Trim() ?? "";
            item.Extras = model.Extras?.Trim() ?? "";
            item.ActivationDate = model.ActivationDate?.Trim() ?? "";
            item.NewExpiryDate = model.NewExpiryDate?.Trim() ?? "";
            item.Notes = model.Notes?.Trim() ?? "";

            var excludingDmg = amount - (model.ExtrasAmount ?? 0m);
            item.ExcludingDmg = excludingDmg;

            var deviceCost = 0m;
            if (item.PaymentFor == "Activation" || item.PaymentFor == "NEW DEVICE" || item.PaymentFor == "Device")
            {
                var device = data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo, item.VehicleNo, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, item.InvoiceId, StringComparison.OrdinalIgnoreCase));
                    deviceCost = invoice?.CostPerDevice ?? device.UnitCost;
                }
            }
            else if (item.PaymentFor == "Reactivation" && config.TryGetValue("PRICE_REACTIVATION", out var reaPrice) && decimal.TryParse(reaPrice, out var configuredReaPrice)) deviceCost = configuredReaPrice;
            item.Profit = excludingDmg - (deviceCost + item.SimCost + item.CommissionCost);

            if (!string.IsNullOrWhiteSpace(item.NewExpiryDate) || !string.IsNullOrWhiteSpace(item.ActivationDate))
            {
                var device = data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo, item.VehicleNo, StringComparison.OrdinalIgnoreCase));
                if (device is not null)
                {
                    if (!string.IsNullOrWhiteSpace(item.NewExpiryDate)) device.LatestExpiryDate = item.NewExpiryDate;
                    if (!string.IsNullOrWhiteSpace(item.ActivationDate)) device.ActivationDate = item.ActivationDate;
                }
            }
        });
        TempData["Success"] = "Transaction updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateBulkSplitViewBagAsync(AppData data, string branch)
    {
        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "12";
        ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "0";

        var vendors = await _db.Vendors
            .Where(v => v.Branch == branch)
            .OrderBy(v => v.Name)
            .ToListAsync();

        var knownVendors = vendors
            .Select(v => v.Name.Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (knownVendors.Count == 0)
        {
            knownVendors = data.Devices
                .Where(dev => dev.Branch == branch && !string.IsNullOrWhiteSpace(dev.Vendor))
                .Select(dev => dev.Vendor.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }

        ViewBag.KnownVendors = knownVendors;
        ViewBag.VendorPrefixMap = vendors
            .GroupBy(v => v.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().EffectiveBatchPrefix, StringComparer.OrdinalIgnoreCase);
    }

    [HttpGet]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> BulkSplit()
    {
        var data = await _store.SnapshotAsync();
        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        await PopulateBulkSplitViewBagAsync(data, branch);
        return View(new BulkSplitViewModel());
    }

    [HttpPost]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> BulkSplit(BulkSplitViewModel model)
    {
        var data = await _store.SnapshotAsync();
        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        if (!ModelState.IsValid)
        {
            await PopulateBulkSplitViewBagAsync(data, branch);
            return View(model);
        }

        var validAllocations = model.BulkAllocations.Where(a => !string.IsNullOrWhiteSpace(a.VehicleNo) && a.Amount > 0).ToList();
        if (validAllocations.Count == 0)
        {
            await PopulateBulkSplitViewBagAsync(data, branch);
            ModelState.AddModelError("BulkAllocations", "Enter at least one valid vehicle allocation.");
            return View(model);
        }

        var batchId = model.BatchId?.Trim() ?? "";
        await _store.UpdateAsync(data =>
        {
            var config = data.Config.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            var simCost = 0m;
            var commissionCost = 0m;
            if (config.TryGetValue("SIM_COST", out var sCost) && decimal.TryParse(sCost, out var parsedSCost)) simCost = parsedSCost;
            if (config.TryGetValue("COMMISSION_COST", out var cCost) && decimal.TryParse(cCost, out var parsedCCost)) commissionCost = parsedCCost;

            var masterPaymentFor = !string.IsNullOrWhiteSpace(model.PaymentFor) && !model.PaymentFor.Equals("Bulk allocation", StringComparison.OrdinalIgnoreCase) && !model.PaymentFor.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? model.PaymentFor.Trim()
                : (branch == "RTO" ? "NEW DEVICE" : "Activation");

            var bulkPartsList = new List<object>();
            decimal totalProfit = 0m;
            decimal totalExcludingDmg = 0m;
            decimal totalSim = 0m;
            decimal totalComm = 0m;

            foreach (var allocation in validAllocations)
            {
                var cleanVeh = allocation.VehicleNo!.Trim();
                var device = data.Devices.FirstOrDefault(item => string.Equals(item.VehicleNo, cleanVeh, StringComparison.OrdinalIgnoreCase));
                var deviceInvoice = device?.InvoiceId ?? "";
                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, deviceInvoice, StringComparison.OrdinalIgnoreCase));
                var deviceCost = (masterPaymentFor == "Activation" || masterPaymentFor == "NEW DEVICE") && device != null ? (invoice?.CostPerDevice ?? device.UnitCost) : 0m;

                var excludingDmg = allocation.Amount - allocation.ExtrasAmount;
                if (string.IsNullOrWhiteSpace(allocation.NewExpiryDate) && !string.IsNullOrWhiteSpace(allocation.ActivationDate))
                {
                    allocation.NewExpiryDate = DeviceLifecycleHelper.CalculateExpiryFromActivation(allocation.ActivationDate, branch, "New", config) ?? "";
                }

                var aProfit = excludingDmg - (deviceCost + simCost + commissionCost);
                totalProfit += aProfit;
                totalExcludingDmg += excludingDmg;
                totalSim += simCost;
                totalComm += commissionCost;

                bulkPartsList.Add(new
                {
                    vehicleNo = cleanVeh,
                    imei = !string.IsNullOrWhiteSpace(allocation.Imei) ? allocation.Imei.Trim() : (device?.Imei ?? ""),
                    @operator = !string.IsNullOrWhiteSpace(allocation.NetworkOperator) ? allocation.NetworkOperator.Trim() : (device?.NetworkOperator ?? ""),
                    amount = allocation.Amount,
                    profit = aProfit,
                    activationDate = allocation.ActivationDate?.Trim() ?? "",
                    newExpiryDate = allocation.NewExpiryDate?.Trim() ?? ""
                });

                if (device is not null)
                {
                    device.Paid = "Yes";
                    if (!string.IsNullOrWhiteSpace(allocation.ActivationDate)) device.ActivationDate = allocation.ActivationDate.Trim();
                    if (!string.IsNullOrWhiteSpace(allocation.NewExpiryDate)) device.LatestExpiryDate = allocation.NewExpiryDate.Trim();
                }
            }

            var jsonParts = System.Text.Json.JsonSerializer.Serialize(bulkPartsList);
            var userNotes = model.Notes?.Trim() ?? "";
            var combinedNotes = string.IsNullOrWhiteSpace(userNotes)
                ? $"Bulk Allocation ({validAllocations.Count} Vehicles): {string.Join(", ", validAllocations.Select(a => a.VehicleNo!.Trim()))} [BULK_PARTS:{jsonParts}]"
                : $"{userNotes} [BULK_PARTS:{jsonParts}]";

            var totalAmt = model.TotalPayment > 0 ? model.TotalPayment : validAllocations.Sum(a => a.Amount);
            var firstDev = data.Devices.FirstOrDefault(item => string.Equals(item.VehicleNo, validAllocations[0].VehicleNo!.Trim(), StringComparison.OrdinalIgnoreCase));

            var masterTxn = new TransactionRecord
            {
                Date = model.Date.ToString("yyyy-MM-dd"),
                VehicleNo = string.Join(", ", validAllocations.Select(a => a.VehicleNo!.Trim())),
                Imei = firstDev?.Imei ?? "",
                Vendor = model.Vendor?.Trim() ?? (firstDev?.Vendor ?? ""),
                InvoiceId = firstDev?.InvoiceId ?? "",
                PaymentMode = model.PaymentMode,
                PaymentFor = masterPaymentFor,
                Amount = totalAmt,
                BatchId = batchId,
                Utr = model.Utr?.Trim() ?? "",
                PaidTo = model.PaidTo?.Trim() ?? "",
                Notes = combinedNotes,
                Extras = model.Extras?.Trim() ?? "None",
                ExcludingDmg = totalExcludingDmg,
                SimCost = totalSim,
                CommissionCost = totalComm,
                Profit = totalProfit,
                Branch = branch
            };
            data.Transactions.Add(masterTxn);
        });
        TempData["Success"] = $"Allocated {validAllocations.Sum(a => a.Amount):C} across {validAllocations.Count} vehicles.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> GetUtrBalance(string utr)
    {
        if (string.IsNullOrWhiteSpace(utr)) return Json(new { total = 0, used = 0 });
        utr = utr.Trim();
        var transactions = (await _store.SnapshotAsync()).Transactions
            .Where(t => string.Equals(t.Utr, utr, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (transactions.Count == 0) return Json(new { total = 0, used = 0 });

        var total = transactions.Sum(t => t.Amount ?? 0m);
        var used = transactions.Sum(t => t.Amount ?? 0m);

        return Json(new { total = total, used = used });
    }

    [HttpPost]
    [AllowAnonymous]
    public IActionResult SyncState([FromBody] object data)
    {
        System.IO.File.AppendAllText("c:\\MYGTS\\mvc_website\\project\\telemetry.log", $"{DateTime.UtcNow}: {data}\n");
        return Ok();
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetDeviceInfo(string vehicleNo)
    {
        if (string.IsNullOrWhiteSpace(vehicleNo)) return Json(new { isFound = false, unitCost = 0, vendor = "", invoiceId = "" });

        var normalizedQuery = vehicleNo.Replace(" ", "").Trim();

        var allDevices = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(_db.Devices).AsNoTracking().ToListAsync();
        var device = allDevices
            .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), normalizedQuery, StringComparison.OrdinalIgnoreCase));

        if (device is null) return Json(new { isFound = false, unitCost = 0, vendor = "", invoiceId = "" });

        var allInvoices = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(_db.Invoices).AsNoTracking().ToListAsync();
        var invoice = allInvoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, device.InvoiceId, StringComparison.OrdinalIgnoreCase));
        var unitCost = invoice != null && invoice.CostPerDevice > 0 ? invoice.CostPerDevice : (invoice != null && invoice.Quantity > 0 ? (invoice.TotalWithTaxes / invoice.Quantity) : device.UnitCost);
        var parsedActivation = DateValue.Parse(device.ActivationDate)?.ToString("yyyy-MM-dd");
        var parsedExpiry = DateValue.Parse(device.LatestExpiryDate)?.ToString("yyyy-MM-dd");

        var dataSnapshot = await _store.SnapshotAsync();
        var configMap = dataSnapshot.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : (device.Branch ?? "DMG");
        var calculatedExpiry = DeviceLifecycleHelper.CalculateExpiryFromActivation(parsedActivation, currentBranch, device.Type, configMap);

        if (string.IsNullOrWhiteSpace(parsedExpiry))
        {
            parsedExpiry = calculatedExpiry ?? DateValue.Parse(DeviceLifecycleHelper.CalculateExpiry(device, configMap))?.ToString("yyyy-MM-dd");
        }

        var normalizedType = device.Type;
        if (!string.IsNullOrWhiteSpace(normalizedType))
        {
            if (normalizedType.Contains("2", StringComparison.OrdinalIgnoreCase) || normalizedType.Equals("New", StringComparison.OrdinalIgnoreCase))
                normalizedType = "New";
            else if (normalizedType.Contains("1", StringComparison.OrdinalIgnoreCase) || normalizedType.Equals("Old", StringComparison.OrdinalIgnoreCase))
                normalizedType = "Old";
        }

        return Json(new { 
            isFound = true, 
            unitCost = unitCost, 
            vendor = device.Vendor, 
            invoiceId = device.InvoiceId, 
            type = normalizedType,
            rawType = device.Type,
            activationDate = parsedActivation ?? "",
            latestExpiryDate = parsedExpiry ?? "",
            calculatedExpiry = calculatedExpiry ?? ""
        });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetInvoiceCost(string invoiceId)
    {
        if (string.IsNullOrWhiteSpace(invoiceId)) return Json(new { unitCost = 0 });
        var data = await _store.SnapshotAsync();
        var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, invoiceId.Trim(), StringComparison.OrdinalIgnoreCase));
        return Json(new { unitCost = invoice?.CostPerDevice ?? 0 });
    }

    [HttpGet("~/Transactions/ExportAll")]
    public async Task<IActionResult> ExportAll([FromQuery] TransactionFilterViewModel filters, [FromQuery] string? branch = null)
    {
        var data = await _store.SnapshotAsync();
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        var sortedTransactions = FilterAndSortTransactions(data, filters);
        var bytes = _spreadsheets.ExportTransactions(sortedTransactions, activeBranch);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"mygts-transactions-{activeBranch.ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    [HttpGet("~/Transactions/DownloadSample")]
    [AllowAnonymous]
    public IActionResult DownloadSample([FromQuery] string? branch = null)
    {
        var activeBranch = (!string.IsNullOrWhiteSpace(branch) ? branch : Request.Cookies["mygts_branch"]) == "RTO" ? "RTO" : "DMG";
        string filename;
        string sheetName;
        string[] headers;
        List<string[]> rows;

        if (activeBranch == "RTO")
        {
            filename = "sample-transactions-rto.xlsx";
            sheetName = "Transactions_RTO";
            headers = ["sl_no", "date", "vehicle_no", "vendor", "payment_mode", "payment_for", "amount", "batch_id", "utr", "paid_to", "activation_date", "new_expiry_date", "profit", "notes"];
            rows =
            [
                ["1", "2026-09-01", "KA01AB5678", "Sample Vendor", "UPI", "Recharge", "1500.00", "BATCH-RTO-01", "UTR123456789", "MYGTS", "2026-09-01", "2027-08-31", "300.00", "Sample RTO payment notes"],
                ["2", "2026-09-02", "KA01AB5679", "Sample Vendor", "Bank transfer", "New Device", "3500.00", "BATCH-RTO-01", "UTR987654321", "MYGTS", "2026-09-02", "2027-09-01", "700.00", "Sample RTO installation payment"]
            ];
        }
        else
        {
            filename = "sample-transactions-dmg.xlsx";
            sheetName = "Transactions_DMG";
            headers = ["sl_no", "date", "vehicle_no", "vendor", "payment_mode", "payment_for", "amount", "batch_id", "utr", "paid_to", "extras", "activation_date", "new_expiry_date", "profit", "notes"];
            rows =
            [
                ["1", "2026-09-01", "KA01AB1234", "Sample Vendor", "UPI", "Recharge", "1500.00", "BATCH-DMG-01", "UTR123456789", "MYGTS", "0", "2026-09-01", "2027-08-31", "300.00", "Sample DMG payment notes"],
                ["2", "2026-09-02", "KA01AB1235", "Sample Vendor", "Bank transfer", "New Device", "3500.00", "BATCH-DMG-01", "UTR987654321", "MYGTS", "200", "2026-09-02", "2027-09-01", "700.00", "Sample DMG installation payment"]
            ];
        }

        var bytes = _spreadsheets.CreateFormattedSampleWorkbook(sheetName, headers, rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }

    [HttpGet]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public IActionResult Import()
    {
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        ViewBag.CurrentBranch = currentBranch;
        return View(new ImportViewModel());
    }

    [HttpPost]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
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

        List<TransactionRecord> parsedTransactions;
        try
        {
            await using var stream = model.Workbook.OpenReadStream();
            parsedTransactions = _spreadsheets.ParseImportedTransactions(stream, model.Workbook.FileName);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(model.Workbook), $"Error reading the file: {ex.Message}");
            return View(model);
        }

        if (parsedTransactions.Count == 0)
        {
            ModelState.AddModelError(nameof(model.Workbook), "No valid transaction rows found in the file. Ensure the file contains headers including 'vehicle_no' and 'date'.");
            return View(model);
        }

        var data = await _store.SnapshotAsync();
        var existingTxns = data.Transactions.Where(t => string.Equals(t.Branch, currentBranch, StringComparison.OrdinalIgnoreCase)).ToList();

        static string NormDate(string? d) => DateValue.Parse(d)?.ToString("yyyy-MM-dd") ?? (d?.Trim() ?? "");
        static string NormVeh(string? v) => (v ?? "").Replace(" ", "").Trim();

        var reviewItems = new List<TransactionImportReviewItemViewModel>();
        int newCount = 0;
        int conflictCount = 0;

        for (int i = 0; i < parsedTransactions.Count; i++)
        {
            var imported = parsedTransactions[i];
            imported.Branch = currentBranch;

            // Primary key conflict check: Vehicle No and Date together
            var existingMatch = existingTxns.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(t.VehicleNo) && !string.IsNullOrWhiteSpace(imported.VehicleNo) &&
                string.Equals(NormVeh(t.VehicleNo), NormVeh(imported.VehicleNo), StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(t.Date) && !string.IsNullOrWhiteSpace(imported.Date) &&
                string.Equals(NormDate(t.Date), NormDate(imported.Date), StringComparison.OrdinalIgnoreCase)
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

            reviewItems.Add(new TransactionImportReviewItemViewModel
            {
                Index = i,
                Imported = imported,
                Existing = existingMatch,
                IsConflict = isConflict,
                Action = isConflict ? "replace" : "insert"
            });
        }

        var reviewModel = new TransactionImportReviewViewModel
        {
            Branch = currentBranch,
            TotalRows = parsedTransactions.Count,
            NewCount = newCount,
            ConflictCount = conflictCount,
            Items = reviewItems,
            StagedTransactionsJson = System.Text.Json.JsonSerializer.Serialize(parsedTransactions)
        };

        return View("ImportReview", reviewModel);
    }

    [HttpPost]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    [RequestFormLimits(ValueCountLimit = 100000, ValueLengthLimit = 50 * 1024 * 1024)]
    public async Task<IActionResult> ConfirmImport(TransactionImportConfirmSubmitModel model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.StagedTransactionsJson))
        {
            TempData["Error"] = "No transaction rows were submitted for import. Please try uploading the file again.";
            return RedirectToAction(nameof(Import));
        }

        var currentBranch = !string.IsNullOrWhiteSpace(model.Branch) ? model.Branch : (Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG");

        List<TransactionRecord>? stagedTransactions;
        try
        {
            stagedTransactions = System.Text.Json.JsonSerializer.Deserialize<List<TransactionRecord>>(model.StagedTransactionsJson);
        }
        catch
        {
            stagedTransactions = null;
        }

        if (stagedTransactions == null || stagedTransactions.Count == 0)
        {
            TempData["Error"] = "No transaction rows found in staged data. Please try uploading the file again.";
            return RedirectToAction(nameof(Import));
        }

        int addedCount = 0;
        int replacedCount = 0;
        int skippedCount = 0;

        static string NormDate(string? d) => DateValue.Parse(d)?.ToString("yyyy-MM-dd") ?? (d?.Trim() ?? "");
        static string NormVeh(string? v) => (v ?? "").Replace(" ", "").Trim();

        await _store.UpdateAsync(current =>
        {
            var maxSlNo = current.Transactions.Count > 0 ? current.Transactions.Max(t => int.TryParse(t.SlNo, out var val) ? val : 0) : 0;

            for (int i = 0; i < stagedTransactions.Count; i++)
            {
                var staged = stagedTransactions[i];
                var action = model.Actions != null && model.Actions.TryGetValue(i, out var act) ? act : "insert";

                if (string.Equals(action, "keep", StringComparison.OrdinalIgnoreCase))
                {
                    skippedCount++;
                    continue;
                }

                if (string.Equals(action, "replace", StringComparison.OrdinalIgnoreCase))
                {
                    // Primary key: VehicleNo + Date together
                    var existing = current.Transactions.FirstOrDefault(t =>
                        !string.IsNullOrWhiteSpace(t.VehicleNo) && !string.IsNullOrWhiteSpace(staged.VehicleNo) &&
                        string.Equals(NormVeh(t.VehicleNo), NormVeh(staged.VehicleNo), StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(t.Date) && !string.IsNullOrWhiteSpace(staged.Date) &&
                        string.Equals(NormDate(t.Date), NormDate(staged.Date), StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        if (!string.IsNullOrWhiteSpace(staged.Vendor)) existing.Vendor = staged.Vendor.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.PaymentMode)) existing.PaymentMode = staged.PaymentMode.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.PaymentFor)) existing.PaymentFor = staged.PaymentFor.Trim();
                        if (staged.Amount > 0) existing.Amount = staged.Amount;
                        if (!string.IsNullOrWhiteSpace(staged.BatchId)) existing.BatchId = staged.BatchId.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Utr)) existing.Utr = staged.Utr.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.PaidTo)) existing.PaidTo = staged.PaidTo.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Extras)) existing.Extras = staged.Extras.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.ActivationDate)) existing.ActivationDate = staged.ActivationDate.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.NewExpiryDate)) existing.NewExpiryDate = staged.NewExpiryDate.Trim();
                        if (!string.IsNullOrWhiteSpace(staged.Notes)) existing.Notes = staged.Notes.Trim();
                        if (staged.Profit != 0) existing.Profit = staged.Profit;

                        // Sync activation and expiry to device if available
                        var device = current.Devices.FirstOrDefault(d => string.Equals(NormVeh(d.VehicleNo), NormVeh(existing.VehicleNo), StringComparison.OrdinalIgnoreCase));
                        if (device != null)
                        {
                            if (!string.IsNullOrWhiteSpace(existing.ActivationDate)) device.ActivationDate = existing.ActivationDate;
                            if (!string.IsNullOrWhiteSpace(existing.NewExpiryDate)) device.LatestExpiryDate = existing.NewExpiryDate;
                        }

                        replacedCount++;
                        continue;
                    }
                }

                // Default: insert as new transaction
                staged.Id = Guid.NewGuid().ToString("N");
                staged.SlNo = (++maxSlNo).ToString();
                staged.Branch = currentBranch;
                if (string.IsNullOrWhiteSpace(staged.Date)) staged.Date = DateTime.UtcNow.ToString("yyyy-MM-dd");
                if (string.IsNullOrWhiteSpace(staged.PaymentMode)) staged.PaymentMode = "UPI";
                if (string.IsNullOrWhiteSpace(staged.PaymentFor)) staged.PaymentFor = "Recharge";
                if (string.IsNullOrWhiteSpace(staged.CreatedAt)) staged.CreatedAt = DateTime.UtcNow.ToString("O");

                // Sync activation and expiry to device if available
                var dev = current.Devices.FirstOrDefault(d => string.Equals(NormVeh(d.VehicleNo), NormVeh(staged.VehicleNo), StringComparison.OrdinalIgnoreCase));
                if (dev != null)
                {
                    if (!string.IsNullOrWhiteSpace(staged.ActivationDate)) dev.ActivationDate = staged.ActivationDate;
                    if (!string.IsNullOrWhiteSpace(staged.NewExpiryDate)) dev.LatestExpiryDate = staged.NewExpiryDate;
                }

                current.Transactions.Add(staged);
                addedCount++;
            }
        });

        TempData["Success"] = $"Import completed for {currentBranch}: {addedCount} new transactions added, {replacedCount} existing transactions updated, {skippedCount} existing transactions kept.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("RecalculateAll")]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> RecalculateAll()
    {
        var currentBranch = HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        int updatedCount = 0;

        await _store.UpdateAsync(data =>
        {
            var config = data.Config.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

            decimal simCost2Year = 0m;
            decimal simCost1Year = 0m;
            decimal commissionCost = 0m;
            decimal rechargePrice = 0m;
            decimal reactivationPrice = 0m;

            if (config.TryGetValue("SIM_COST", out var s2) && decimal.TryParse(s2, out var ps2)) simCost2Year = ps2;
            if (config.TryGetValue("SIM_COST_1_YEAR", out var s1) && decimal.TryParse(s1, out var ps1)) simCost1Year = ps1;
            if (config.TryGetValue("COMMISSION_COST", out var cc) && decimal.TryParse(cc, out var pcc)) commissionCost = pcc;
            if (config.TryGetValue("PRICE_RECHARGE", out var pr) && decimal.TryParse(pr, out var ppr)) rechargePrice = ppr;
            if (config.TryGetValue("PRICE_REACTIVATION", out var pra) && decimal.TryParse(pra, out var ppra)) reactivationPrice = ppra;

            var invoicesByNo = data.Invoices
                .Where(i => !string.IsNullOrWhiteSpace(i.InvoiceNo))
                .GroupBy(i => i.InvoiceNo.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var devicesByVeh = data.Devices
                .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo))
                .GroupBy(d => d.VehicleNo.Replace(" ", "").Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var txn in data.Transactions)
            {
                var normVeh = (txn.VehicleNo ?? "").Replace(" ", "").Trim();
                devicesByVeh.TryGetValue(normVeh, out var dev);

                var invNo = (txn.InvoiceId ?? dev?.InvoiceId ?? "").Trim();
                invoicesByNo.TryGetValue(invNo, out var inv);

                var deviceCost = 0m;
                var paymentFor = (txn.PaymentFor ?? "").Trim();

                if (paymentFor.Equals("Activation", StringComparison.OrdinalIgnoreCase) ||
                    paymentFor.Equals("NEW DEVICE", StringComparison.OrdinalIgnoreCase) ||
                    paymentFor.Equals("Device", StringComparison.OrdinalIgnoreCase))
                {
                    deviceCost = inv?.CostPerDevice ?? dev?.UnitCost ?? 0m;
                }
                else if (paymentFor.Equals("Reactivation", StringComparison.OrdinalIgnoreCase))
                {
                    deviceCost = reactivationPrice;
                }
                else if (paymentFor.Equals("Recharge", StringComparison.OrdinalIgnoreCase))
                {
                    deviceCost = 0m;
                }

                var applicableSimCost = 0m;
                var applicableCommCost = 0m;

                if (currentBranch == "RTO")
                {
                    var isOld = string.Equals(txn.Type, "Old", StringComparison.OrdinalIgnoreCase);
                    applicableSimCost = isOld ? simCost1Year : simCost2Year;
                    applicableCommCost = commissionCost;
                }
                else
                {
                    applicableSimCost = txn.SimCost;
                    applicableCommCost = txn.CommissionCost;
                }

                txn.SimCost = applicableSimCost;
                txn.CommissionCost = applicableCommCost;

                var amount = txn.Amount ?? 0m;
                var excludingDmg = txn.ExcludingDmg ?? amount;
                txn.ExcludingDmg = excludingDmg;

                txn.Profit = excludingDmg - (deviceCost + applicableSimCost + applicableCommCost);
                updatedCount++;
            }
        });

        TempData["Success"] = $"Recalculated {updatedCount} transactions for {currentBranch} branch based on current configuration.";
        return RedirectToAction(nameof(Index));
    }
}
