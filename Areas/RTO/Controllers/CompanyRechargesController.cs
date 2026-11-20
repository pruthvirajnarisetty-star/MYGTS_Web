using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MygtsMvc.Data;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Areas.RTO.Controllers;
using Microsoft.AspNetCore.Mvc;

[Area("RTO")]
[Authorize]
[RequireModulePermission("CompanyRecharges", PermissionLevel.View)]
public sealed class CompanyRechargesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly SpreadsheetService _spreadsheets;
    private readonly IAppDataStore _store;

    public CompanyRechargesController(ApplicationDbContext db, SpreadsheetService spreadsheets, IAppDataStore store)
    {
        _db = db;
        _spreadsheets = spreadsheets;
        _store = store;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        var branch = context.HttpContext.Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        if (branch == "RTO")
        {
            context.Result = RedirectToAction("Index", "Dashboard");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? query = null, [FromQuery] string? provider = null)
    {
        var batchesQuery = _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(provider))
        {
            if (string.Equals(provider, "BlueMine", StringComparison.OrdinalIgnoreCase))
            {
                batchesQuery = batchesQuery.Where(b => b.Provider == "BlueMine");
            }
            else
            {
                batchesQuery = batchesQuery.Where(b => b.Provider == "MYGTS" || b.Provider == "Taisys" || string.IsNullOrEmpty(b.Provider));
            }
        }

        var allBatches = await batchesQuery.OrderByDescending(b => b.Date).ThenByDescending(b => b.CreatedAt).ToListAsync();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            allBatches = allBatches.Where(b =>
                b.PoNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.PayReferenceNo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Notes.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Items.Any(i =>
                    i.VehicleNo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    i.Imei.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    i.Iccid.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    i.RequestId.Contains(q, StringComparison.OrdinalIgnoreCase)
                )
            ).ToList();
        }

        // Global KPI Computations
        var allDbBatches = await _db.CompanyRechargeBatches.AsNoTracking().ToListAsync();
        var totalAdvance = allDbBatches.Sum(b => b.AdvanceAmount);
        var totalConsumed = allDbBatches.Sum(b => b.TotalAmount);
        var totalBalance = totalAdvance - totalConsumed;
        var totalBatchesCount = allDbBatches.Count;
        var totalDevicesCount = allDbBatches.Sum(b => b.TotalDevices);

        // Batches with remaining balance that can be transferred
        var availableSources = allDbBatches
            .Where(b => b.RemainingBalance > 0)
            .OrderByDescending(b => b.Date)
            .ToList();

        // Config rates
        var configData = await _store.SnapshotAsync();
        var configMap = configData.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        decimal rechargeRate = 1298m;
        decimal reactRate = 1534m;
        if (configMap.TryGetValue("COMPANY_RECHARGE_RATE", out var rStr) && decimal.TryParse(rStr, out var rVal))
            rechargeRate = rVal;
        if (configMap.TryGetValue("COMPANY_REACTIVATION_RATE", out var raStr) && decimal.TryParse(raStr, out var raVal))
            reactRate = raVal;

        // BlueMine-specific rates (company recharge rates)
        decimal bluemineRechargeRate = 1156.4m;
        decimal bluemineReactRate = 1392.4m;
        if (configMap.TryGetValue("BLUEMINE_RECHARGE_RATE", out var brStr) && decimal.TryParse(brStr, out var brVal))
            bluemineRechargeRate = brVal;
        if (configMap.TryGetValue("BLUEMINE_REACTIVATION_RATE", out var braStr) && decimal.TryParse(braStr, out var braVal))
            bluemineReactRate = braVal;

        // Per-provider KPI split
        var allBluemineBatches = allDbBatches.Where(b => string.Equals(b.Provider, "BlueMine", StringComparison.OrdinalIgnoreCase)).ToList();
        var allMygtsProviderBatches = allDbBatches.Where(b => !string.Equals(b.Provider, "BlueMine", StringComparison.OrdinalIgnoreCase)).ToList();

        var vm = new CompanyRechargesIndexViewModel
        {
            Query = query,
            FilterProvider = provider,
            TotalAdvanceOverall = totalAdvance,
            TotalConsumedOverall = totalConsumed,
            TotalBalanceOverall = totalBalance,
            TotalBatchesCount = totalBatchesCount,
            TotalDevicesCount = totalDevicesCount,
            Batches = allBatches,
            AvailableSourceBatchesForTransfer = availableSources,
            DefaultRechargeRate = rechargeRate,
            DefaultReactivationRate = reactRate,
            BluemineRechargeRate = bluemineRechargeRate,
            BluemineReactivationRate = bluemineReactRate,
            BluemineAdvance = allBluemineBatches.Sum(b => b.AdvanceAmount),
            BluemineConsumed = allBluemineBatches.Sum(b => b.TotalAmount),
            BluemineBatchCount = allBluemineBatches.Count,
            BluemineDevicesCount = allBluemineBatches.Sum(b => b.TotalDevices),
            MygtsAdvance = allMygtsProviderBatches.Sum(b => b.AdvanceAmount),
            MygtsConsumed = allMygtsProviderBatches.Sum(b => b.TotalAmount),
            MygtsBatchCount = allMygtsProviderBatches.Count,
            MygtsDevicesCount = allMygtsProviderBatches.Sum(b => b.TotalDevices)
        };

        return View(vm);
    }

    private (decimal rechargeRate, decimal reactRate) GetRatesForProvider(Dictionary<string, string> configMap, string? provider)
    {
        if (string.Equals(provider, "BlueMine", StringComparison.OrdinalIgnoreCase))
        {
            decimal bRecharge = 1156.4m;
            decimal bReact = 1392.4m;
            if (configMap.TryGetValue("BLUEMINE_RECHARGE_RATE", out var brStr) && decimal.TryParse(brStr, out var brVal))
                bRecharge = brVal;
            if (configMap.TryGetValue("BLUEMINE_REACTIVATION_RATE", out var braStr) && decimal.TryParse(braStr, out var braVal))
                bReact = braVal;
            return (bRecharge, bReact);
        }
        else
        {
            decimal recharge = 1298m;
            decimal react = 1534m;
            if (configMap.TryGetValue("COMPANY_RECHARGE_RATE", out var rStr) && decimal.TryParse(rStr, out var rVal))
                recharge = rVal;
            if (configMap.TryGetValue("COMPANY_REACTIVATION_RATE", out var raStr) && decimal.TryParse(raStr, out var raVal))
                react = raVal;
            return (recharge, react);
        }
    }

    [HttpGet]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> Create([FromQuery] string? sourceBatchId = null, [FromQuery] string? provider = null)
    {
        var configData = await _store.SnapshotAsync();
        var configMap = configData.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        var activeProvider = string.Equals(provider, "BlueMine", StringComparison.OrdinalIgnoreCase) ? "BlueMine" : "MYGTS";
        var (rechargeRate, reactRate) = GetRatesForProvider(configMap, activeProvider);

        var (bRecharge, bReact) = GetRatesForProvider(configMap, "BlueMine");
        var (mRecharge, mReact) = GetRatesForProvider(configMap, "MYGTS");
        ViewBag.BluemineRechargeRate = bRecharge;
        ViewBag.BluemineReactRate = bReact;
        ViewBag.MygtsRechargeRate = mRecharge;
        ViewBag.MygtsReactRate = mReact;

        var availableSources = await _db.CompanyRechargeBatches
            .Where(b => b.RemainingBalance > 0)
            .OrderByDescending(b => b.Date)
            .ToListAsync();
        ViewBag.AvailableSources = availableSources;

        var vm = new CompanyRechargeBatchFormViewModel
        {
            Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Provider = activeProvider,
            DefaultRechargeRate = rechargeRate,
            DefaultReactivationRate = reactRate
        };

        if (!string.IsNullOrWhiteSpace(sourceBatchId))
        {
            var src = availableSources.FirstOrDefault(b => b.Id == sourceBatchId);
            if (src != null)
            {
                vm.TransferredFromBatchId = src.Id;
                vm.TransferredFromBatchPo = src.PoNumber;
                vm.CarriedForwardAmount = src.RemainingBalance;
            }
        }

        return View(vm);
    }

    [HttpPost]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> Create([FromForm] CompanyRechargeBatchFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.PoNumber))
            return BadRequest(new { error = "PO Number is required." });

        var batch = new CompanyRechargeBatch
        {
            Id = Guid.NewGuid().ToString("N"),
            Branch = _db.CurrentBranch,
            PoNumber = model.PoNumber.Trim(),
            PayReferenceNo = model.PayReferenceNo.Trim(),
            Date = string.IsNullOrWhiteSpace(model.Date) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : model.Date.Trim(),
            Provider = string.IsNullOrWhiteSpace(model.Provider) ? "Taisys" : model.Provider.Trim(),
            AdvanceAmount = model.AdvanceAmount,
            CarriedForwardAmount = model.CarriedForwardAmount,
            TransferredFromBatchId = model.TransferredFromBatchId,
            TransferredFromBatchPo = model.TransferredFromBatchPo,
            Notes = (model.Notes ?? "").Trim(),
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        // If carrying forward from a source batch, deduct from that source batch
        if (!string.IsNullOrWhiteSpace(model.TransferredFromBatchId) && model.CarriedForwardAmount > 0)
        {
            var srcBatch = await _db.CompanyRechargeBatches.FirstOrDefaultAsync(b => b.Id == model.TransferredFromBatchId);
            if (srcBatch != null)
            {
                srcBatch.CarriedOutAmount += model.CarriedForwardAmount;
                srcBatch.TransferredToBatchId = batch.Id;
                srcBatch.TransferredToBatchPo = batch.PoNumber;
                srcBatch.RemainingBalance = srcBatch.EffectiveAdvance - srcBatch.TotalAmount;
            }
        }

        batch.TotalDevices = 0;
        batch.RenewalCount = 0;
        batch.ReactivationCount = 0;
        batch.TotalAmount = 0m;
        batch.RemainingBalance = batch.EffectiveAdvance;

        _db.CompanyRechargeBatches.Add(batch);
        await _db.SaveChangesAsync();

        if (Request.Headers.XRequestedWith == "XMLHttpRequest" || Request.ContentType?.Contains("json") == true)
        {
            return Json(new { success = true, id = batch.Id, redirectUrl = Url.Action(nameof(Index), new { expandBatchId = batch.Id }) });
        }

        TempData["Success"] = $"Batch '{batch.PoNumber}' created successfully. Click 'Add Device' on the batch to enter devices.";
        return RedirectToAction(nameof(Index), new { expandBatchId = batch.Id });
    }

    public class AddSingleDeviceModel
    {
        public string BatchId { get; set; } = string.Empty;
        public string? VehicleNo { get; set; }
        public string? Imei { get; set; }
        public string? Iccid { get; set; }
        public string? RequestId { get; set; }
        public string? ActionType { get; set; }
        public decimal? Amount { get; set; }
    }

    [HttpPost]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> AddSingleDevice([FromForm] AddSingleDeviceModel model)
    {
        var batch = await _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == model.BatchId);

        if (batch == null)
            return NotFound(new { success = false, message = "Batch not found." });

        var vehicleNo = (model.VehicleNo ?? "").Trim();
        var imei = (model.Imei ?? "").Trim();
        var iccid = (model.Iccid ?? "").Trim();

        if (string.IsNullOrWhiteSpace(vehicleNo) && string.IsNullOrWhiteSpace(imei) && string.IsNullOrWhiteSpace(iccid))
        {
            if (Request.Headers.XRequestedWith == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json"))
                return Json(new { success = false, message = "Please enter at least a Vehicle Number, IMEI, or ICCID." });
            TempData["Error"] = "Please enter at least a Vehicle Number, IMEI, or ICCID.";
            return RedirectToAction(nameof(Index), new { expandBatchId = batch.Id });
        }

        var configData = await _store.SnapshotAsync();
        var configMap = configData.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        var (rechargeRate, reactRate) = GetRatesForProvider(configMap, batch.Provider);

        var isReact = string.Equals(model.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase);
        var rate = (model.Amount.HasValue && model.Amount.Value > 0)
            ? model.Amount.Value
            : (isReact ? reactRate : rechargeRate);

        int sl = batch.Items.Count > 0 ? batch.Items.Max(i => i.SlNo) + 1 : 1;

        var newItem = new CompanyRechargeItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Branch = _db.CurrentBranch,
            BatchId = batch.Id,
            SlNo = sl,
            VehicleNo = vehicleNo,
            Imei = imei,
            Iccid = iccid,
            RequestId = string.IsNullOrWhiteSpace(model.RequestId) ? "-" : model.RequestId.Trim(),
            ActionType = isReact ? "Reactivation" : "Renewal",
            PerDeviceAmount = rate,
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        batch.Items.Add(newItem);

        // Recalculate totals
        batch.TotalDevices = batch.Items.Count;
        batch.RenewalCount = batch.Items.Count(i => !string.Equals(i.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase));
        batch.ReactivationCount = batch.Items.Count(i => string.Equals(i.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase));
        batch.TotalAmount = batch.Items.Sum(i => i.PerDeviceAmount);
        batch.RemainingBalance = batch.EffectiveAdvance - batch.TotalAmount;

        await _db.SaveChangesAsync();

        var allBatches = await _db.CompanyRechargeBatches.AsNoTracking().ToListAsync();
        var totalAdvanceOverall = allBatches.Sum(b => b.AdvanceAmount);
        var totalConsumedOverall = allBatches.Sum(b => b.TotalAmount);
        var totalBalanceOverall = totalAdvanceOverall - totalConsumedOverall;
        var totalDevicesOverall = allBatches.Sum(b => b.TotalDevices);

        if (Request.Headers.XRequestedWith == "XMLHttpRequest" || Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return Json(new
            {
                success = true,
                message = "Device added successfully.",
                item = new
                {
                    id = newItem.Id,
                    slNo = newItem.SlNo,
                    vehicleNo = newItem.VehicleNo,
                    imei = newItem.Imei,
                    iccid = newItem.Iccid,
                    requestId = newItem.RequestId,
                    actionType = newItem.ActionType,
                    amount = newItem.PerDeviceAmount
                },
                batch = new
                {
                    id = batch.Id,
                    totalDevices = batch.TotalDevices,
                    totalAmount = batch.TotalAmount,
                    remainingBalance = batch.RemainingBalance,
                    renewalCount = batch.RenewalCount,
                    reactivationCount = batch.ReactivationCount,
                    isLowBalance = batch.RemainingBalance > 0 && batch.RemainingBalance < rechargeRate
                },
                overall = new
                {
                    totalAdvance = totalAdvanceOverall,
                    totalConsumed = totalConsumedOverall,
                    totalBalance = totalBalanceOverall,
                    totalDevices = totalDevicesOverall
                }
            });
        }

        TempData["Success"] = $"Added device {(string.IsNullOrWhiteSpace(vehicleNo) ? imei : vehicleNo)} to batch '{batch.PoNumber}'.";
        return RedirectToAction(nameof(Index), new { expandBatchId = batch.Id });
    }

    [HttpGet]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> AddDevices(string batchId)
    {
        var batch = await _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == batchId);

        if (batch == null) return NotFound();

        var configData = await _store.SnapshotAsync();
        var configMap = configData.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        var (rechargeRate, reactRate) = GetRatesForProvider(configMap, batch.Provider);

        var vm = new CompanyRechargeAddDevicesViewModel
        {
            BatchId = batch.Id,
            PoNumber = batch.PoNumber,
            Provider = batch.Provider,
            Date = batch.Date,
            AdvanceAmount = batch.AdvanceAmount,
            CarriedForwardAmount = batch.CarriedForwardAmount,
            CarriedOutAmount = batch.CarriedOutAmount,
            TotalAmountExisting = batch.TotalAmount,
            RemainingBalance = batch.RemainingBalance,
            ExistingDeviceCount = batch.Items.Count,
            DefaultRechargeRate = rechargeRate,
            DefaultReactivationRate = reactRate
        };

        return View(vm);
    }

    [HttpPost]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> AddDevices([FromForm] CompanyRechargeAddDevicesViewModel model)
    {
        var batch = await _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == model.BatchId);

        if (batch == null) return NotFound();

        int addedCount = 0;
        int sl = batch.Items.Count > 0 ? batch.Items.Max(i => i.SlNo) + 1 : 1;

        if (model.Items != null && model.Items.Count > 0)
        {
            foreach (var item in model.Items)
            {
                var vehicleNo = (item.VehicleNo ?? "").Trim();
                var imei = (item.Imei ?? "").Trim();
                var iccid = (item.Iccid ?? "").Trim();

                if (string.IsNullOrWhiteSpace(vehicleNo) && string.IsNullOrWhiteSpace(imei) && string.IsNullOrWhiteSpace(iccid))
                    continue;

                var isReact = string.Equals(item.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase);
                var rate = item.PerDeviceAmount > 0 
                    ? item.PerDeviceAmount 
                    : (isReact ? model.DefaultReactivationRate : model.DefaultRechargeRate);

                batch.Items.Add(new CompanyRechargeItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Branch = _db.CurrentBranch,
                    BatchId = batch.Id,
                    SlNo = sl++,
                    VehicleNo = vehicleNo,
                    Imei = imei,
                    Iccid = iccid,
                    RequestId = string.IsNullOrWhiteSpace(item.RequestId) ? "-" : item.RequestId.Trim(),
                    ActionType = isReact ? "Reactivation" : "Renewal",
                    PerDeviceAmount = rate,
                    CreatedAt = DateTime.UtcNow.ToString("O")
                });

                addedCount++;
            }
        }

        // Recalculate batch totals
        batch.TotalDevices = batch.Items.Count;
        batch.RenewalCount = batch.Items.Count(i => !string.Equals(i.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase));
        batch.ReactivationCount = batch.Items.Count(i => string.Equals(i.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase));
        batch.TotalAmount = batch.Items.Sum(i => i.PerDeviceAmount);
        batch.RemainingBalance = batch.EffectiveAdvance - batch.TotalAmount;

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Successfully added {addedCount} device(s) to batch '{batch.PoNumber}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> DeleteDevice([FromForm] string itemId, [FromForm] string batchId)
    {
        var batch = await _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == batchId);

        if (batch == null) return NotFound();

        var item = batch.Items.FirstOrDefault(i => i.Id == itemId);
        if (item != null)
        {
            _db.CompanyRechargeItems.Remove(item);
            batch.Items.Remove(item);

            int sl = 1;
            foreach (var remaining in batch.Items.OrderBy(i => i.SlNo))
            {
                remaining.SlNo = sl++;
            }

            batch.TotalDevices = batch.Items.Count;
            batch.RenewalCount = batch.Items.Count(i => !string.Equals(i.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase));
            batch.ReactivationCount = batch.Items.Count(i => string.Equals(i.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase));
            batch.TotalAmount = batch.Items.Sum(i => i.PerDeviceAmount);
            batch.RemainingBalance = batch.EffectiveAdvance - batch.TotalAmount;

            await _db.SaveChangesAsync();
            TempData["Success"] = "Device removed from batch.";
        }

        return RedirectToAction(nameof(Index), new { expandBatchId = batchId });
    }

    [HttpGet]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(string id)
    {
        var batch = await _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (batch == null) return NotFound();

        var configData = await _store.SnapshotAsync();
        var configMap = configData.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        var (rechargeRate, reactRate) = GetRatesForProvider(configMap, batch.Provider);

        var vm = new CompanyRechargeBatchFormViewModel
        {
            Id = batch.Id,
            PoNumber = batch.PoNumber,
            PayReferenceNo = batch.PayReferenceNo,
            Date = batch.Date,
            Provider = batch.Provider,
            AdvanceAmount = batch.AdvanceAmount,
            CarriedForwardAmount = batch.CarriedForwardAmount,
            TransferredFromBatchId = batch.TransferredFromBatchId,
            TransferredFromBatchPo = batch.TransferredFromBatchPo,
            Notes = batch.Notes,
            DefaultRechargeRate = rechargeRate,
            DefaultReactivationRate = reactRate,
            Items = batch.Items.OrderBy(i => i.SlNo).Select(i => new CompanyRechargeItemInputModel
            {
                SlNo = i.SlNo,
                VehicleNo = i.VehicleNo,
                Imei = i.Imei,
                Iccid = i.Iccid,
                RequestId = i.RequestId,
                ActionType = i.ActionType,
                PerDeviceAmount = i.PerDeviceAmount
            }).ToList()
        };

        return View(vm);
    }

    [HttpPost]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> Edit(string id, [FromForm] CompanyRechargeBatchFormViewModel model)
    {
        var batch = await _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (batch == null) return NotFound();

        batch.PoNumber = (model.PoNumber ?? "").Trim();
        batch.PayReferenceNo = (model.PayReferenceNo ?? "").Trim();
        batch.Date = string.IsNullOrWhiteSpace(model.Date) ? batch.Date : model.Date.Trim();
        batch.Provider = string.IsNullOrWhiteSpace(model.Provider) ? "Taisys" : model.Provider.Trim();
        batch.AdvanceAmount = model.AdvanceAmount;
        batch.Notes = (model.Notes ?? "").Trim();

        // Clear existing items and repopulate
        _db.CompanyRechargeItems.RemoveRange(batch.Items);
        batch.Items.Clear();

        int renewalCount = 0;
        int reactCount = 0;
        decimal totalAmount = 0m;
        int sl = 1;

        if (model.Items != null && model.Items.Count > 0)
        {
            foreach (var item in model.Items)
            {
                var isReact = string.Equals(item.ActionType, "Reactivation", StringComparison.OrdinalIgnoreCase);
                if (isReact) reactCount++;
                else renewalCount++;

                totalAmount += item.PerDeviceAmount;

                batch.Items.Add(new CompanyRechargeItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Branch = _db.CurrentBranch,
                    BatchId = batch.Id,
                    SlNo = sl++,
                    VehicleNo = (item.VehicleNo ?? "").Trim(),
                    Imei = (item.Imei ?? "").Trim(),
                    Iccid = (item.Iccid ?? "").Trim(),
                    RequestId = string.IsNullOrWhiteSpace(item.RequestId) ? "-" : item.RequestId.Trim(),
                    ActionType = isReact ? "Reactivation" : "Renewal",
                    PerDeviceAmount = item.PerDeviceAmount,
                    CreatedAt = DateTime.UtcNow.ToString("O")
                });
            }
        }

        batch.TotalDevices = batch.Items.Count;
        batch.RenewalCount = renewalCount;
        batch.ReactivationCount = reactCount;
        batch.TotalAmount = totalAmount;
        batch.RemainingBalance = batch.EffectiveAdvance - totalAmount;

        await _db.SaveChangesAsync();

        if (Request.Headers.XRequestedWith == "XMLHttpRequest" || Request.ContentType?.Contains("json") == true)
        {
            return Json(new { success = true, redirectUrl = Url.Action(nameof(Index)) });
        }

        TempData["Success"] = $"Batch '{batch.PoNumber}' updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> Delete(string id)
    {
        var batch = await _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (batch != null)
        {
            _db.CompanyRechargeItems.RemoveRange(batch.Items);
            _db.CompanyRechargeBatches.Remove(batch);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Batch '{batch.PoNumber}' was deleted successfully.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [RequireModulePermission("CompanyRecharges", PermissionLevel.Edit)]
    public async Task<IActionResult> TransferBalance([FromForm] string sourceBatchId, [FromForm] string targetBatchId, [FromForm] decimal amount)
    {
        if (amount <= 0)
        {
            TempData["Error"] = "Transfer amount must be greater than zero.";
            return RedirectToAction(nameof(Index));
        }

        var source = await _db.CompanyRechargeBatches.FirstOrDefaultAsync(b => b.Id == sourceBatchId);
        var target = await _db.CompanyRechargeBatches.FirstOrDefaultAsync(b => b.Id == targetBatchId);

        if (source == null || target == null)
        {
            TempData["Error"] = "Source or Target batch was not found.";
            return RedirectToAction(nameof(Index));
        }

        if (source.RemainingBalance < amount)
        {
            TempData["Error"] = $"Cannot transfer {amount:C}. Source batch only has {source.RemainingBalance:C} remaining.";
            return RedirectToAction(nameof(Index));
        }

        // Deduct from source
        source.CarriedOutAmount += amount;
        source.TransferredToBatchId = target.Id;
        source.TransferredToBatchPo = target.PoNumber;
        source.RemainingBalance = source.EffectiveAdvance - source.TotalAmount;

        // Credit to target
        target.CarriedForwardAmount += amount;
        target.TransferredFromBatchId = source.Id;
        target.TransferredFromBatchPo = source.PoNumber;
        target.RemainingBalance = target.EffectiveAdvance - target.TotalAmount;

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Successfully transferred {amount:C} from {source.PoNumber} to {target.PoNumber} to prevent wasted balance.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> LookupDevice([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Json(new { found = false });

        var clean = query.Replace(" ", "").Trim();
        var allDevices = await _db.Devices.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        var device = allDevices.FirstOrDefault(d =>
            (!string.IsNullOrWhiteSpace(d.Imei) && d.Imei.Trim().Equals(clean, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(d.Iccid) && d.Iccid.Trim().Equals(clean, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(d.VehicleNo) && d.VehicleNo.Replace(" ", "").Trim().Equals(clean, StringComparison.OrdinalIgnoreCase))
        );

        if (device == null)
            return Json(new { found = false });

        return Json(new
        {
            found = true,
            vehicleNo = device.VehicleNo,
            imei = device.Imei,
            iccid = device.Iccid,
            networkOperator = device.NetworkOperator,
            vendor = device.Vendor
        });
    }

    [HttpGet]
    public async Task<IActionResult> ExportBatch(string id)
    {
        var batch = await _db.CompanyRechargeBatches
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (batch == null) return NotFound();

        var bytes = _spreadsheets.ExportCompanyRechargeBatch(batch, batch.Items);
        var filename = $"Company_Recharge_{(string.IsNullOrWhiteSpace(batch.PoNumber) ? "Batch" : batch.PoNumber.Replace("/", "-"))}_{DateTime.UtcNow:yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", filename);
    }
}
