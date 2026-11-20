using Microsoft.AspNetCore.Authorization;















using Microsoft.AspNetCore.Mvc;















using Microsoft.EntityFrameworkCore;















using MygtsMvc.Models;















using MygtsMvc.Services;















using MygtsMvc.ViewModels;















using System.IO;































namespace MygtsMvc.Areas.RTO.Controllers;















using Microsoft.AspNetCore.Mvc;































[Area("RTO")]















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















        var deviceLifecycleMap = data.Devices















            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo))















            .GroupBy(d => d.VehicleNo.Trim(), StringComparer.OrdinalIgnoreCase)















            .ToDictionary(















                g => g.Key, 















                g => !string.IsNullOrWhiteSpace(g.First().LifecycleStatus) ? g.First().LifecycleStatus! : (!string.IsNullOrWhiteSpace(g.First().Status) ? g.First().Status! : "Active"), 















                StringComparer.OrdinalIgnoreCase);































        if (!string.IsNullOrWhiteSpace(filters.Query))















        {















            var terms = filters.Query.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries);















            transactions = transactions.Where(item =>















            {















                var lc = deviceLifecycleMap.TryGetValue(item.VehicleNo?.Trim() ?? "", out var s) ? s : "";















                var combined = string.Join(" ",















                    item.VehicleNo, item.Imei, lc, item.Vendor, item.Utr, item.BatchId,















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

        if (!string.IsNullOrWhiteSpace(filters.TransactionType) && !string.Equals(filters.TransactionType, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(filters.TransactionType, "Bulk", StringComparison.OrdinalIgnoreCase) || string.Equals(filters.TransactionType, "BulkAllocation", StringComparison.OrdinalIgnoreCase))
            {
                transactions = transactions.Where(t => 
                    (!string.IsNullOrWhiteSpace(t.PaymentFor) && t.PaymentFor.Equals("Bulk allocation", StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(t.Notes) && t.Notes.Contains("[BULK_PARTS:")));
            }
            else if (string.Equals(filters.TransactionType, "Merge", StringComparison.OrdinalIgnoreCase) || string.Equals(filters.TransactionType, "MergePartPayments", StringComparison.OrdinalIgnoreCase))
            {
                transactions = transactions.Where(t => 
                    (!string.IsNullOrWhiteSpace(t.Notes) && (t.Notes.Contains("[MERGE_PARTS:") || t.Notes.Contains("Split Payment:"))) ||
                    (!string.IsNullOrWhiteSpace(t.PaymentMode) && t.PaymentMode.Contains('+')));
            }
            else if (string.Equals(filters.TransactionType, "Standard", StringComparison.OrdinalIgnoreCase) || string.Equals(filters.TransactionType, "Normal", StringComparison.OrdinalIgnoreCase))
            {
                transactions = transactions.Where(t => 
                    (string.IsNullOrWhiteSpace(t.PaymentFor) || !t.PaymentFor.Equals("Bulk allocation", StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(t.Notes) || (!t.Notes.Contains("[BULK_PARTS:") && !t.Notes.Contains("[MERGE_PARTS:"))) &&
                    (string.IsNullOrWhiteSpace(t.PaymentMode) || !t.PaymentMode.Contains('+')));
            }
        }


        // Date Range Filtering on transaction Date
        if (!string.IsNullOrWhiteSpace(filters.FromDate) || !string.IsNullOrWhiteSpace(filters.ToDate))
        {
            var from = !string.IsNullOrWhiteSpace(filters.FromDate) ? filters.FromDate.Trim() : null;
            var to = !string.IsNullOrWhiteSpace(filters.ToDate) ? filters.ToDate.Trim() : null;

            transactions = transactions.Where(item =>
            {
                if (string.IsNullOrWhiteSpace(item.Date)) return false;
                var dStr = item.Date.Trim();
                if (DateTime.TryParse(dStr, out var parsedDt))
                {
                    dStr = parsedDt.ToString("yyyy-MM-dd");
                }
                if (from != null && string.Compare(dStr, from, StringComparison.Ordinal) < 0) return false;
                if (to != null && string.Compare(dStr, to, StringComparison.Ordinal) > 0) return false;
                return true;
            });
        }































        return filters.SortColumn?.ToLowerInvariant() switch















        {















            "date" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => DateValue.Parse(t.Date)) : transactions.OrderBy(t => DateValue.Parse(t.Date)),















            "vehicle" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.VehicleNo) : transactions.OrderBy(t => t.VehicleNo),
            "imei" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => t.Imei) : transactions.OrderBy(t => t.Imei),































            "lifecycle" => filters.SortDirection == "desc" ? transactions.OrderByDescending(t => deviceLifecycleMap.TryGetValue(t.VehicleNo?.Trim() ?? "", out var s) ? s : "") : transactions.OrderBy(t => deviceLifecycleMap.TryGetValue(t.VehicleNo?.Trim() ?? "", out var s) ? s : ""),















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















    public async Task<IActionResult> Index(TransactionFilterViewModel filters, [FromQuery] bool clear = false)















    {















        var activeBranch = "RTO";















        var filterCookieKey = $"mygts_filter_{activeBranch}_transactions";































        if (clear)















        {















            Response.Cookies.Delete(filterCookieKey);















            filters = new TransactionFilterViewModel















            {















                Page = 1,















                PageSize = filters.PageSize > 0 ? filters.PageSize : 20















            };















        }















        else















        {















            var hasExplicitFilters = Request.Query.Keys.Any(k =>















                !string.Equals(k, "clear", StringComparison.OrdinalIgnoreCase) &&















                !string.IsNullOrWhiteSpace(Request.Query[k]));































            if (hasExplicitFilters)















            {















                var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);















                if (!string.IsNullOrWhiteSpace(filters.Query)) dict["Query"] = filters.Query;















                if (!string.IsNullOrWhiteSpace(filters.PaymentFor)) dict["PaymentFor"] = filters.PaymentFor;
                if (!string.IsNullOrWhiteSpace(filters.TransactionType)) dict["TransactionType"] = filters.TransactionType;















                if (!string.IsNullOrWhiteSpace(filters.FilterVendor)) dict["FilterVendor"] = filters.FilterVendor;















                if (!string.IsNullOrWhiteSpace(filters.PaymentMode)) dict["PaymentMode"] = filters.PaymentMode;
                if (!string.IsNullOrWhiteSpace(filters.FromDate)) dict["FromDate"] = filters.FromDate;
                if (!string.IsNullOrWhiteSpace(filters.ToDate)) dict["ToDate"] = filters.ToDate;















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















        var allTxns = data.Transactions.AsEnumerable().Reverse().ToList();















        var deviceLifecycleMap = data.Devices















            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo))















            .GroupBy(d => d.VehicleNo.Trim(), StringComparer.OrdinalIgnoreCase)















            .ToDictionary(















                g => g.Key, 















                g => !string.IsNullOrWhiteSpace(g.First().LifecycleStatus) ? g.First().LifecycleStatus! : (!string.IsNullOrWhiteSpace(g.First().Status) ? g.First().Status! : "Active"), 















                StringComparer.OrdinalIgnoreCase);















        ViewBag.DeviceLifecycleMap = deviceLifecycleMap;
        ViewBag.VehicleImeiMap = data.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && !string.IsNullOrWhiteSpace(d.Imei))
            .GroupBy(d => d.VehicleNo.Replace(" ", "").Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.IsNotInUse ? 1 : 0).ThenByDescending(d => d.CreatedAt).First().Imei, StringComparer.OrdinalIgnoreCase);































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































        ViewBag.DeviceOperators = data.Devices

            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && !string.IsNullOrWhiteSpace(d.NetworkOperator))

            .DistinctBy(d => d.VehicleNo.Replace(" ", "").Trim(), StringComparer.OrdinalIgnoreCase)

            .ToDictionary(d => d.VehicleNo, d => d.NetworkOperator, StringComparer.OrdinalIgnoreCase);

        ViewBag.VendorCredits = (data.VendorCredits ?? new List<VendorCreditRecord>())
            .OrderByDescending(c => c.Status == "Active")
            .ThenByDescending(c => c.Date)
            .ThenByDescending(c => c.CreatedAt)
            .ToList();
        ViewBag.TotalActiveCredit = (data.VendorCredits ?? new List<VendorCreditRecord>())
            .Where(c => c.Status == "Active")
            .Sum(c => c.RemainingBalance);

        return View(new TransactionFilterViewModel















        {















            Query = filters.Query,
            FromDate = filters.FromDate,
            ToDate = filters.ToDate,















            PaymentMode = filters.PaymentMode,















            FilterVendor = filters.FilterVendor,















            PaymentFor = filters.PaymentFor,















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















            PaymentForOptions = new[] { "Recharge", "Activation", "Reactivation", "Other" }.Union(allTxns.Select(t => t.PaymentFor).Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList()















        });















    }































    [HttpGet]















    [RequireModulePermission("Transactions", PermissionLevel.Edit)]















    public async Task<IActionResult> Create(string? vehicleNo = null, string? paymentFor = null, string? rechargeId = null, string? deviceId = null, string? imei = null)
    {
        var data = await _store.SnapshotAsync();
        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
        var currentArea = (ControllerContext.RouteData.Values["area"]?.ToString() ?? "RTO").ToUpper();
        var freshConfigs = await _db.Config.IgnoreQueryFilters().Where(c => c.Branch == currentArea).AsNoTracking().ToListAsync();
        foreach (var fc in freshConfigs) { if (!string.IsNullOrWhiteSpace(fc.Key) && !string.IsNullOrWhiteSpace(fc.Value)) config[fc.Key] = fc.Value; }

        ViewBag.PriceRecharge = config.TryGetValue("PRICE_RECHARGE", out var pr) ? pr : "1298";
        ViewBag.CompanyRechargeRate = config.TryGetValue("COMPANY_RECHARGE_RATE", out var cr) ? cr : "1298";
        ViewBag.CompanyReactRate = config.TryGetValue("COMPANY_REACTIVATION_RATE", out var cra) ? cra : "1534";
        ViewBag.BluemineRechargeRate = config.TryGetValue("BLUEMINE_RECHARGE_RATE", out var br) ? br : "1156.40";
        ViewBag.BluemineReactRate = config.TryGetValue("BLUEMINE_REACTIVATION_RATE", out var bra) ? bra : "1392.40";
        ViewBag.SmtRechargeRate = config.TryGetValue("SMT_RECHARGE_RATE", out var smtr) ? smtr : "1298";
        ViewBag.SmtReactRate = config.TryGetValue("SMT_REACTIVATION_RATE", out var smtra) ? smtra : "1534";
        ViewBag.SensoriseRechargeRate = config.TryGetValue("SENSORISE_RECHARGE_RATE", out var senr) ? senr : "1298";
        ViewBag.SensoriseReactRate = config.TryGetValue("SENSORISE_REACTIVATION_RATE", out var senra) ? senra : "1534";
        ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";
        ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";
        ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";
        ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";
        ViewBag.AdminCost = config.TryGetValue("ADMIN_COST", out var ac) ? ac : "0";

        ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "12";
        ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "0";
        ViewBag.ActivationExpiry1YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_MONTHS", out var m1) ? m1 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_1_YEAR", out var m1b) ? m1b : "12");
        ViewBag.ActivationExpiry1YearDays = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_DAYS", out var d1) ? d1 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_1_YEAR", out var d1b) ? d1b : "0");
        ViewBag.ActivationExpiry2YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_MONTHS", out var m2) ? m2 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_2_YEAR", out var m2b) ? m2b : "24");
        ViewBag.ActivationExpiry2YearDays = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_DAYS", out var d2) ? d2 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_2_YEAR", out var d2b) ? d2b : "0");
        ViewBag.ActivationExpiryMonths1Year = ViewBag.ActivationExpiry1YearMonths;
        ViewBag.ActivationExpiryDays1Year = ViewBag.ActivationExpiry1YearDays;
        ViewBag.ActivationExpiryMonths2Year = ViewBag.ActivationExpiry2YearMonths;
        ViewBag.ActivationExpiryDays2Year = ViewBag.ActivationExpiry2YearDays;

        await PopulateBulkSplitViewBagAsync(data, "RTO");

        ViewBag.VendorCredits = (data.VendorCredits ?? new List<VendorCreditRecord>())
            .Where(c => c.Status == "Active")
            .OrderByDescending(c => c.Date)
            .ThenByDescending(c => c.CreatedAt)
            .ToList();
        ViewBag.TotalActiveCredit = (data.VendorCredits ?? new List<VendorCreditRecord>())
            .Where(c => c.Status == "Active")
            .Sum(c => c.RemainingBalance);
        var model = new TransactionFormViewModel();
        if (!string.IsNullOrWhiteSpace(vehicleNo) || !string.IsNullOrWhiteSpace(rechargeId) || !string.IsNullOrWhiteSpace(deviceId) || !string.IsNullOrWhiteSpace(imei))
        {
            if (!string.IsNullOrWhiteSpace(vehicleNo))
            {
                vehicleNo = DeviceLifecycleHelper.ParseVehicleSlash(vehicleNo).VehicleNo;
                model.VehicleNo = vehicleNo.Trim();
            }
            RechargeRecord? rec = null;
            if (!string.IsNullOrWhiteSpace(rechargeId))
            {
                rec = data.Recharges.FirstOrDefault(r => r.Id == rechargeId);
            }
            if (rec == null && !string.IsNullOrWhiteSpace(vehicleNo))
            {
                rec = data.Recharges
                    .Where(r => string.Equals(r.VehicleNo?.Trim(), vehicleNo.Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.RechargeDate)
                    .ThenByDescending(r => r.CreatedAt)
                    .FirstOrDefault();
            }
            if (rec != null)
            {
                if (string.IsNullOrWhiteSpace(model.VehicleNo))
                {
                    model.VehicleNo = rec.VehicleNo ?? "";
                }
                if (!string.IsNullOrWhiteSpace(rec.RechargeDate))
                {
                    var pRecDate = DateValue.Parse(rec.RechargeDate);
                    if (pRecDate.HasValue) model.Date = pRecDate.Value.Date;
                }
                if (!string.IsNullOrWhiteSpace(rec.ActivationDate))
                {
                    model.ActivationDate = DateValue.Parse(rec.ActivationDate)?.ToString("yyyy-MM-dd") ?? rec.ActivationDate;
                }
                model.PaymentFor = !string.IsNullOrWhiteSpace(paymentFor) ? paymentFor : "None";
                model.Vendor = rec.Vendor ?? "";
                model.Imei = rec.Imei ?? "";
            }
            DeviceRecord? dev = null;
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                dev = data.Devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            }
            if (dev == null)
            {
                var devLookup = !string.IsNullOrWhiteSpace(model.VehicleNo) ? model.VehicleNo : vehicleNo;
                if (!string.IsNullOrWhiteSpace(devLookup))
                {
                    var cleanVeh = devLookup.Replace(" ", "");
                    dev = data.Devices.FirstOrDefault(d => string.Equals((d.VehicleNo ?? "").Replace(" ", ""), cleanVeh, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (dev == null && !string.IsNullOrWhiteSpace(imei))
            {
                var cleanImei = imei.Trim();
                dev = data.Devices.FirstOrDefault(d => string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase));
            }
            var allDevs = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(_db.Devices).AsNoTracking().ToListAsync();
            if (dev == null)
            {
                var devLookup = !string.IsNullOrWhiteSpace(model.VehicleNo) ? model.VehicleNo : vehicleNo;
                var cleanVeh = (devLookup ?? "").Replace(" ", "").Trim();
                var cleanImei = (imei ?? "").Trim();
                dev = allDevs.FirstOrDefault(d =>
                    (!string.IsNullOrWhiteSpace(deviceId) && string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(cleanImei) && string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(cleanVeh) && string.Equals((d.VehicleNo ?? "").Replace(" ", "").Trim(), cleanVeh, StringComparison.OrdinalIgnoreCase)));
            }
            if (dev != null)
            {
                if (string.IsNullOrWhiteSpace(model.VehicleNo) && !string.IsNullOrWhiteSpace(dev.VehicleNo)) model.VehicleNo = dev.VehicleNo;
                if (!string.IsNullOrWhiteSpace(dev.NetworkOperator)) model.NetworkOperator = dev.NetworkOperator;
                ViewBag.NetworkOperator = dev.NetworkOperator ?? "";
                var devLc = !string.IsNullOrWhiteSpace(dev.LifecycleStatus) ? dev.LifecycleStatus : (dev.Status ?? "");
                ViewBag.DeviceLifecycle = devLc;
                ViewBag.Lifecycle = devLc;
                if (string.IsNullOrWhiteSpace(model.Vendor)) model.Vendor = dev.Vendor ?? "";
                model.InvoiceId = dev.InvoiceId ?? "";
                if (string.IsNullOrWhiteSpace(model.PaymentFor)) model.PaymentFor = !string.IsNullOrWhiteSpace(paymentFor) ? paymentFor : "None";
                if (string.IsNullOrWhiteSpace(model.Imei)) model.Imei = dev.Imei ?? "";
                if (!string.IsNullOrWhiteSpace(dev.Type))
                {
                    var rawT = dev.Type.Trim();
                    model.Type = (rawT.Contains("2", StringComparison.OrdinalIgnoreCase) || rawT.Equals("New", StringComparison.OrdinalIgnoreCase)) ? "New" : "Old";
                }
                else
                {
                    model.Type = "New";
                }
            }
            else
            {
                model.Type = "New";
            }

            var (resAct, resExp, _) = DeviceLifecycleHelper.ResolveDatesForTransaction(
                model.VehicleNo,
                DateTime.UtcNow,
                "RTO",
                data.Recharges,
                allDevs,
                config,
                model.PaymentFor,
                dev?.Imei ?? model.Imei);

            if (!string.IsNullOrWhiteSpace(resAct)) model.ActivationDate = resAct;
            if (!string.IsNullOrWhiteSpace(resExp)) model.NewExpiryDate = resExp;
        }
        return View(model);
    }

    [HttpPost]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> Create(TransactionFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Type)) model.Type = "New";
        if (model.CurrentMode == EntryMode.MergePartPayments)
        {
            ModelState.Remove(nameof(model.Amount));
            ModelState.Remove(nameof(model.Date));
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
            var cleanImei = model.Imei?.Trim();
            var deviceExists = snap.Devices.Any(d => (!string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), normalized, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(cleanImei) && string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase)));
            if (!deviceExists) ModelState.AddModelError("VehicleNo", "Not available in the data");
        }
        if (!ModelState.IsValid)
        {
            var data = await _store.SnapshotAsync();
            var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);
            ViewBag.PriceRecharge = config.TryGetValue("PRICE_RECHARGE", out var pr) ? pr : "1298";
            ViewBag.CompanyRechargeRate = config.TryGetValue("COMPANY_RECHARGE_RATE", out var cr) ? cr : "1298";
            ViewBag.CompanyReactRate = config.TryGetValue("COMPANY_REACTIVATION_RATE", out var cra) ? cra : "1534";
            ViewBag.BluemineRechargeRate = config.TryGetValue("BLUEMINE_RECHARGE_RATE", out var br) ? br : "1156.40";
            ViewBag.BluemineReactRate = config.TryGetValue("BLUEMINE_REACTIVATION_RATE", out var bra) ? bra : "1392.40";
            ViewBag.SmtRechargeRate = config.TryGetValue("SMT_RECHARGE_RATE", out var smtr) ? smtr : "1298";
            ViewBag.SmtReactRate = config.TryGetValue("SMT_REACTIVATION_RATE", out var smtra) ? smtra : "1534";
            ViewBag.SensoriseRechargeRate = config.TryGetValue("SENSORISE_RECHARGE_RATE", out var senr) ? senr : "1298";
            ViewBag.SensoriseReactRate = config.TryGetValue("SENSORISE_REACTIVATION_RATE", out var senra) ? senra : "1534";
            ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";
            ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";
            ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";
            ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";
            ViewBag.AdminCost = config.TryGetValue("ADMIN_COST", out var ac) ? ac : "0";
            ViewBag.KnownVendors = data.Devices
                .Where(d => !string.IsNullOrWhiteSpace(d.Vendor))
                .Select(d => d.Vendor!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();
            ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "12";
            ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "0";
            ViewBag.ActivationExpiry1YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_MONTHS", out var m1) ? m1 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_1_YEAR", out var m1b) ? m1b : "12");
            ViewBag.ActivationExpiry1YearDays = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_DAYS", out var d1) ? d1 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_1_YEAR", out var d1b) ? d1b : "0");
            ViewBag.ActivationExpiry2YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_MONTHS", out var m2) ? m2 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_2_YEAR", out var m2b) ? m2b : "24");
            ViewBag.ActivationExpiry2YearDays = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_DAYS", out var d2) ? d2 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_2_YEAR", out var d2b) ? d2b : "0");
            ViewBag.ActivationExpiryMonths1Year = ViewBag.ActivationExpiry1YearMonths;
            ViewBag.ActivationExpiryDays1Year = ViewBag.ActivationExpiry1YearDays;
            ViewBag.ActivationExpiryMonths2Year = ViewBag.ActivationExpiry2YearMonths;
            ViewBag.ActivationExpiryDays2Year = ViewBag.ActivationExpiry2YearDays;
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















                var cleanVehStd = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim();
                var cleanImeiStd = (model.Imei ?? "").Trim();
                var cleanIccidStd = (model.Iccid ?? "").Trim();
                DeviceRecord? device = null;
                if (!string.IsNullOrWhiteSpace(cleanImeiStd) && !string.IsNullOrWhiteSpace(cleanIccidStd))
                {
                    device = data.Devices.FirstOrDefault(item => string.Equals((item.Imei ?? "").Trim(), cleanImeiStd, StringComparison.OrdinalIgnoreCase) && string.Equals((item.Iccid ?? "").Trim(), cleanIccidStd, StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && !string.IsNullOrWhiteSpace(cleanImeiStd))
                {
                    device = data.Devices.FirstOrDefault(item => string.Equals((item.Imei ?? "").Trim(), cleanImeiStd, StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && !string.IsNullOrWhiteSpace(cleanVehStd))
                {
                    device = data.Devices.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.VehicleNo) && string.Equals(item.VehicleNo.Replace(" ", "").Trim(), cleanVehStd.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && (!string.IsNullOrWhiteSpace(cleanImeiStd) || !string.IsNullOrWhiteSpace(cleanVehStd)))
                {
                    var crossMatch = _db.Devices.IgnoreQueryFilters().FirstOrDefault(item =>
                        (!string.IsNullOrWhiteSpace(cleanImeiStd) && !string.IsNullOrWhiteSpace(cleanIccidStd) && string.Equals((item.Imei ?? "").Trim(), cleanImeiStd, StringComparison.OrdinalIgnoreCase) && string.Equals((item.Iccid ?? "").Trim(), cleanIccidStd, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(cleanImeiStd) && string.Equals((item.Imei ?? "").Trim(), cleanImeiStd, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(cleanVehStd) && !string.IsNullOrWhiteSpace(item.VehicleNo) && string.Equals(item.VehicleNo.Replace(" ", "").Trim(), cleanVehStd.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));
                    if (crossMatch != null)
                    {
                        crossMatch.Branch = "RTO";
                        device = crossMatch;
                    }
                }















                var deviceInvoice = !string.IsNullOrWhiteSpace(model.InvoiceId) ? model.InvoiceId.Trim() : (device?.InvoiceId ?? "");

                var effectiveVendor = !string.IsNullOrWhiteSpace(model.Vendor) ? model.Vendor.Trim() : (device?.Vendor ?? "");















                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, deviceInvoice, StringComparison.OrdinalIgnoreCase));































                var isActivation = model.PaymentFor == "Activation" || model.PaymentFor == "NEW DEVICE" || model.PaymentFor == "Device";















                var adminCost = 0m;















                if (isActivation)















                {















                    if (device != null && device.AdminCost > 0m)















                    {















                        adminCost = device.AdminCost;















                    }















                    else if (config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc))















                    {















                        adminCost = parsedAc;















                        if (device != null && device.AdminCost == 0m && adminCost > 0m)















                        {















                            device.AdminCost = adminCost;















                        }















                    }















                }































                var devOp = !string.IsNullOrWhiteSpace(model.NetworkOperator) ? model.NetworkOperator : (device?.NetworkOperator ?? "");

                var isReact = string.Equals(model.PaymentFor, "Reactivation", StringComparison.OrdinalIgnoreCase);

                var isRecharge = string.Equals(model.PaymentFor, "Recharge", StringComparison.OrdinalIgnoreCase) || string.Equals(model.PaymentFor, "Renewal", StringComparison.OrdinalIgnoreCase);



                var masterDeviceCost = 0m;

                if (isActivation)

                {

                    masterDeviceCost = invoice?.CostPerDevice ?? device?.UnitCost ?? 0m;

                }

                else if (isReact)

                {

                    masterDeviceCost = config.TryGetValue("PRICE_REACTIVATION", out var reaStr) && decimal.TryParse(reaStr, out var reaVal) ? reaVal : 1500m;

                }

                else if (isRecharge)

                {

                    masterDeviceCost = config.TryGetValue("PRICE_RECHARGE", out var prStr) && decimal.TryParse(prStr, out var prVal) ? prVal : 1500m;

                }

                else

                {

                    masterDeviceCost = 0m;

                }



                var simCost = 0m;

                var commissionCost = 0m;

                var currentBranch = "RTO";



                var simKey = (model.Type == "Old") ? "SIM_COST_1_YEAR" : "SIM_COST";

                if (config.TryGetValue(simKey, out var sCost) && decimal.TryParse(sCost, out var parsedSCost)) simCost = parsedSCost;

                else simCost = (model.Type == "Old") ? 1180.00m : 2360.00m;



                if (config.TryGetValue("COMMISSION_COST", out var cCost) && decimal.TryParse(cCost, out var parsedCCost)) commissionCost = parsedCCost;

                else commissionCost = 4770.00m;































                foreach (var p in validPayments)

                {

                    if (string.Equals(p.PaymentMode?.Trim(), "Online", StringComparison.OrdinalIgnoreCase))

                    {

                        p.PaymentMode = "Banking";

                    }

                }



                var combinedModes = string.Join(" + ", validPayments.Select(p => p.PaymentMode?.Trim()).Where(m => !string.IsNullOrEmpty(m)));















                var combinedUtrs = string.Join(" + ", validPayments.Select(p => p.Utr?.Trim()).Where(u => !string.IsNullOrEmpty(u)));















                var combinedPaidTo = string.Join(" / ", validPayments.Select(p => p.PaidTo?.Trim()).Where(pt => !string.IsNullOrEmpty(pt)).Distinct());















                var partsSummary = string.Join(" + ", validPayments.Select(p => $"{p.Amount:F0} ({p.PaymentMode.Trim()})"));

                var mergePartsJson = System.Text.Json.JsonSerializer.Serialize(validPayments.Select(p => new {

                    date = !string.IsNullOrWhiteSpace(p.Date) ? p.Date.Trim() : (model.Date != default ? model.Date.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd")),

                    amount = p.Amount,

                    mode = p.PaymentMode?.Trim() ?? "",

                    utr = p.Utr?.Trim() ?? "",

                    paidTo = p.PaidTo?.Trim() ?? ""

                }));



                var pendingRemainder = (model.TotalPayment.HasValue && model.TotalPayment.Value > totalAmount)

                    ? (model.TotalPayment.Value - totalAmount)

                    : 0m;



                var balanceNote = pendingRemainder > 0

                    ? $" [PENDING_BALANCE:₹{pendingRemainder:N2}]"

                    : ((model.TotalPayment.HasValue && totalAmount > model.TotalPayment.Value) ? $" [OVERPAID:₹{totalAmount - model.TotalPayment.Value:N2}]" : "");



                var notes = $"Split Payment: {partsSummary} [MERGE_PARTS:{mergePartsJson}]" + balanceNote + (string.IsNullOrWhiteSpace(model.Notes) ? "" : " " + model.Notes.Trim());



                var finalAmount = totalAmount;



                var extras = (string.IsNullOrWhiteSpace(model.Extras) || model.Extras == "0") ? "None" : model.Extras.Trim();

                var extrasDeduction = (extras != "None" && model.ExtrasAmount.HasValue) ? model.ExtrasAmount.Value : 0m;

                var excludingDmg = finalAmount - extrasDeduction;















                var profit = currentBranch == "DMG"















                    ? (excludingDmg - masterDeviceCost - adminCost)



                        : (excludingDmg - (masterDeviceCost + simCost + commissionCost + adminCost));































                if (string.IsNullOrWhiteSpace(model.NewExpiryDate) && !string.IsNullOrWhiteSpace(model.ActivationDate))















                {















                    model.NewExpiryDate = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate, currentBranch, model.Type, config) ?? "";















                }

                var latestPartDate = validPayments.Select(p => p.Date?.Trim()).LastOrDefault(d => !string.IsNullOrWhiteSpace(d));

                var effectiveTxnDate = !string.IsNullOrWhiteSpace(latestPartDate) ? latestPartDate : (model.Date != default ? model.Date.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd"));

































                var transaction = new TransactionRecord















                {















                    Date = effectiveTxnDate,















                    SlNo = device?.SlNo ?? "",















                    VehicleNo = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim(),















                    Imei = !string.IsNullOrWhiteSpace(model.Imei) ? model.Imei.Trim() : (device?.Imei ?? ""),















                    Vendor = effectiveVendor,















                    InvoiceId = deviceInvoice,















                    Type = model.Type ?? "New",















                    PaymentMode = string.IsNullOrWhiteSpace(combinedModes) ? "Merged" : combinedModes,















                    PaymentFor = model.PaymentFor ?? (currentBranch == "RTO" ? "NEW DEVICE" : "Activation"),















                    Amount = finalAmount,















                    BatchId = batchId,















                    Utr = combinedUtrs,















                    PaidTo = combinedPaidTo,















                    ActivationDate = model.ActivationDate?.Trim() ?? "",















                    NewExpiryDate = model.NewExpiryDate?.Trim() ?? "",















                    Notes = notes,















                    ExcludingDmg = excludingDmg,















                    SimCost = simCost,















                    CommissionCost = commissionCost,















                    AdminCost = adminCost,

                    Extras = extras,

                    NetworkOperator = devOp, Profit = profit,

                    IsManualDeviceCost = model.IsManualDeviceCost,

                    ManualDeviceCost = model.ManualDeviceCost

                };



                data.Transactions.Add(transaction);



                var cleanVeh = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim();

                var cleanImei = (model.Imei ?? "").Trim();



                if (pendingRemainder > 0)

                {

                    data.ManualReminders.Add(new ManualReminder

                    {

                        Id = Guid.NewGuid().ToString("N"),

                        Title = $"PAYMENT DUE: {cleanVeh} - BALANCE ₹{pendingRemainder:N2}",

                        Details = $"Customer pending payment on vehicle {cleanVeh}. Expected Total: ₹{(model.TotalPayment ?? totalAmount):N2}, Received: ₹{totalAmount:N2}, Balance Due: ₹{pendingRemainder:N2}. Vendor: {model.Vendor}. Transaction Date: {model.Date:yyyy-MM-dd}. Remind customer to pay remaining amount.",

                        DueDate = model.Date.ToString("yyyy-MM-dd"),

                        Branch = currentBranch,

                        CreatedBy = User.Identity?.Name ?? "System",

                        IsComplete = false,

                        TargetAudience = "@all",

                        CreatedAt = DateTime.UtcNow.ToString("O")

                    });

                }































                var deviceToUpdate = data.Devices.FirstOrDefault(item => (!string.IsNullOrWhiteSpace(cleanVeh) && string.Equals(item.VehicleNo, cleanVeh, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(cleanImei) && string.Equals(item.Imei, cleanImei, StringComparison.OrdinalIgnoreCase)));















                if (deviceToUpdate is not null)















                {















                    if (string.IsNullOrWhiteSpace(deviceToUpdate.VehicleNo) && !string.IsNullOrWhiteSpace(cleanVeh)) deviceToUpdate.VehicleNo = cleanVeh;

                    if (deviceToUpdate.LifecycleStatus == "Inventory" || string.IsNullOrWhiteSpace(deviceToUpdate.LifecycleStatus)) { deviceToUpdate.LifecycleStatus = "Active"; deviceToUpdate.Status = "Active"; }

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















                var currentBranch = "RTO"; // RTO area always uses RTO branch for calculations















                var simKey = (currentBranch == "RTO" && model.Type == "Old") ? "SIM_COST_1_YEAR" : "SIM_COST";















                if (config.TryGetValue(simKey, out var sCost) && decimal.TryParse(sCost, out var parsedSCost)) simCost = parsedSCost;















                if (config.TryGetValue("COMMISSION_COST", out var cCost) && decimal.TryParse(cCost, out var parsedCCost)) commissionCost = parsedCCost;































                var isActivation = model.PaymentFor == "Activation" || model.PaymentFor == "NEW DEVICE" || model.PaymentFor == "Device";















                var configAdminCost = 0m;















                if (isActivation && config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc)) configAdminCost = parsedAc;































                var batchId = model.BatchId?.Trim() ?? "";















                
                var masterPaymentFor = !string.IsNullOrWhiteSpace(model.PaymentFor) && !model.PaymentFor.Equals("Bulk allocation", StringComparison.OrdinalIgnoreCase) && !model.PaymentFor.Equals("None", StringComparison.OrdinalIgnoreCase)
                    ? model.PaymentFor.Trim()
                    : "NEW DEVICE";

                var bulkPartsList = new List<object>();
                decimal totalProfit = 0m;
                decimal totalExcludingDmg = 0m;
                decimal totalSim = 0m;
                decimal totalComm = 0m;
                decimal totalAdmin = 0m;

                foreach (var allocation in validAllocations)
                {
                    var excludingDmg = allocation.Amount - allocation.ExtrasAmount;
                    if (string.IsNullOrWhiteSpace(allocation.NewExpiryDate) || string.IsNullOrWhiteSpace(allocation.ActivationDate))
                    {
                        var (resAct, resExp, _) = DeviceLifecycleHelper.ResolveDatesForTransaction(
                            allocation.VehicleNo,
                            model.Date,
                            currentBranch,
                            data.Recharges,
                            data.Devices,
                            config);
                        if (string.IsNullOrWhiteSpace(allocation.ActivationDate)) allocation.ActivationDate = resAct;
                        if (string.IsNullOrWhiteSpace(allocation.NewExpiryDate)) allocation.NewExpiryDate = resExp;
                    }

                    var cleanVeh = (allocation.VehicleNo ?? "").Replace(" ", "").Trim();
                    var cleanAllocImei = (allocation.Imei ?? "").Trim();
                    var cleanAllocIccid = (allocation.Iccid ?? "").Trim();
                    DeviceRecord? device = null;
                    if (!string.IsNullOrWhiteSpace(cleanAllocImei) && !string.IsNullOrWhiteSpace(cleanAllocIccid))
                    {
                        device = data.Devices.FirstOrDefault(item => string.Equals((item.Imei ?? "").Trim(), cleanAllocImei, StringComparison.OrdinalIgnoreCase) && string.Equals((item.Iccid ?? "").Trim(), cleanAllocIccid, StringComparison.OrdinalIgnoreCase));
                    }
                    if (device == null && !string.IsNullOrWhiteSpace(cleanAllocImei))
                    {
                        device = data.Devices.FirstOrDefault(item => string.Equals((item.Imei ?? "").Trim(), cleanAllocImei, StringComparison.OrdinalIgnoreCase));
                    }
                    if (device == null && !string.IsNullOrWhiteSpace(cleanVeh))
                    {
                        device = data.Devices.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.VehicleNo) && item.VehicleNo.Replace(" ", "").Equals(cleanVeh, StringComparison.OrdinalIgnoreCase));
                    }
                    if (device == null && (!string.IsNullOrWhiteSpace(cleanAllocImei) || !string.IsNullOrWhiteSpace(cleanVeh)))
                    {
                        var crossMatch = _db.Devices.IgnoreQueryFilters().FirstOrDefault(item =>
                            (!string.IsNullOrWhiteSpace(cleanAllocImei) && !string.IsNullOrWhiteSpace(cleanAllocIccid) && string.Equals((item.Imei ?? "").Trim(), cleanAllocImei, StringComparison.OrdinalIgnoreCase) && string.Equals((item.Iccid ?? "").Trim(), cleanAllocIccid, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrWhiteSpace(cleanAllocImei) && string.Equals((item.Imei ?? "").Trim(), cleanAllocImei, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrWhiteSpace(cleanVeh) && !string.IsNullOrWhiteSpace(item.VehicleNo) && item.VehicleNo.Replace(" ", "").Equals(cleanVeh, StringComparison.OrdinalIgnoreCase)));
                        if (crossMatch != null)
                        {
                            crossMatch.Branch = "RTO";
                            device = crossMatch;
                        }
                    }

                    var deviceInvoice = device?.InvoiceId ?? "";
                    var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, deviceInvoice, StringComparison.OrdinalIgnoreCase));
                    var deviceCost = (isActivation && device != null) ? (invoice?.CostPerDevice ?? device.UnitCost) : 0m;
                    var adminCost = (isActivation && device != null && device.AdminCost > 0m) ? device.AdminCost : configAdminCost;

                    var rowAmount = allocation.Amount;
                    excludingDmg = rowAmount - allocation.ExtrasAmount;
                    var profit = excludingDmg - (deviceCost + simCost + commissionCost + adminCost);

                    totalProfit += profit;
                    totalExcludingDmg += excludingDmg;
                    totalSim += simCost;
                    totalComm += commissionCost;
                    totalAdmin += adminCost;

                    bulkPartsList.Add(new
                    {
                        vehicleNo = allocation.VehicleNo!.Trim(),
                        imei = !string.IsNullOrWhiteSpace(allocation.Imei) ? allocation.Imei.Trim() : (device?.Imei ?? ""),
                        @operator = !string.IsNullOrWhiteSpace(allocation.NetworkOperator) ? allocation.NetworkOperator.Trim() : (device?.NetworkOperator ?? ""),
                        amount = rowAmount,
                        profit = profit,
                        activationDate = allocation.ActivationDate?.Trim() ?? "",
                        newExpiryDate = allocation.NewExpiryDate?.Trim() ?? ""
                    });

                    if (device is not null)
                    {
                        device.Paid = "Yes";
                        if (!string.IsNullOrWhiteSpace(allocation.ActivationDate)) device.ActivationDate = allocation.ActivationDate.Trim();
                        if (!string.IsNullOrWhiteSpace(allocation.NewExpiryDate)) device.LatestExpiryDate = allocation.NewExpiryDate.Trim();
                    }

                    var matchingRec = data.Recharges
                        .Where(r => string.Equals(r.VehicleNo?.Replace(" ", "").Trim(), allocation.VehicleNo.Replace(" ", "").Trim(), StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(r => r.RechargeDate)
                        .ThenByDescending(r => r.CreatedAt)
                        .FirstOrDefault();

                    if (matchingRec != null)
                    {
                        matchingRec.PaymentStatus = "Paid";
                        matchingRec.RechargeStatus = "Processed";
                        if (!string.IsNullOrWhiteSpace(allocation.ActivationDate)) matchingRec.ActivationDate = allocation.ActivationDate.Trim();
                        if (!string.IsNullOrWhiteSpace(allocation.NewExpiryDate)) matchingRec.ExpiryDate = allocation.NewExpiryDate.Trim();
                    }
                }

                var jsonParts = System.Text.Json.JsonSerializer.Serialize(bulkPartsList);
                var userNotes = model.Notes?.Trim() ?? "";
                var combinedNotes = string.IsNullOrWhiteSpace(userNotes)
                    ? $"Bulk Allocation ({validAllocations.Count} Vehicles): {string.Join(", ", validAllocations.Select(a => a.VehicleNo!.Trim()))} [BULK_PARTS:{jsonParts}]"
                    : $"{userNotes} [BULK_PARTS:{jsonParts}]";

                var firstDev = data.Devices.FirstOrDefault(item => string.Equals((item.VehicleNo ?? "").Replace(" ", ""), validAllocations[0].VehicleNo!.Replace(" ", "").Trim(), StringComparison.OrdinalIgnoreCase));
                var effVendor = !string.IsNullOrWhiteSpace(model.Vendor) ? model.Vendor.Trim() : (firstDev?.Vendor ?? "");

                // Advance Credit Handling
                decimal advanceApplied = 0m;
                VendorCreditRecord? matchedCredit = null;
                if (model.ApplyAdvanceCredit)
                {
                    if (!string.IsNullOrWhiteSpace(model.AdvanceCreditId))
                    {
                        matchedCredit = (data.VendorCredits ?? new List<VendorCreditRecord>()).FirstOrDefault(c => c.Id == model.AdvanceCreditId.Trim());
                    }
                    if (matchedCredit == null && !string.IsNullOrWhiteSpace(effVendor))
                    {
                        matchedCredit = (data.VendorCredits ?? new List<VendorCreditRecord>())
                            .Where(c => string.Equals((c.Vendor ?? "").Trim(), effVendor, StringComparison.OrdinalIgnoreCase) && c.RemainingBalance > 0 && c.Status == "Active")
                            .OrderByDescending(c => c.Date)
                            .ThenByDescending(c => c.CreatedAt)
                            .FirstOrDefault();
                    }

                    if (matchedCredit != null && matchedCredit.RemainingBalance > 0)
                    {
                        var reqAmt = model.AdvanceCreditAmount.HasValue && model.AdvanceCreditAmount.Value > 0
                            ? model.AdvanceCreditAmount.Value
                            : matchedCredit.RemainingBalance;
                        advanceApplied = Math.Min(matchedCredit.RemainingBalance, reqAmt);

                        matchedCredit.RemainingBalance -= advanceApplied;
                        if (matchedCredit.RemainingBalance <= 0m)
                        {
                            matchedCredit.RemainingBalance = 0m;
                            matchedCredit.Status = "Settled";
                            matchedCredit.SettledAt = DateTime.UtcNow.ToString("O");
                        }
                    }
                }

                if (advanceApplied > 0 && matchedCredit != null)
                {
                    var advNote = $"[ADVANCE_USED: {advanceApplied:F2} from {matchedCredit.Date} (UTR: {matchedCredit.Utr}, Mode: {matchedCredit.PaymentMode}, Paid To: {matchedCredit.PaidTo})]";
                    combinedNotes = $"{combinedNotes} {advNote}";
                }

                // Multiple UTR / Payment Handling for Bulk Allocation
                var validPayments = (model.BulkPayments ?? new List<MergePaymentRow>())
                    .Where(p => p.Amount > 0 || !string.IsNullOrWhiteSpace(p.Utr))
                    .ToList();

                var totalAllocated = validAllocations.Sum(a => a.Amount);
                var rawPayment = model.TotalPayment ?? totalAllocated;
                var effectivePaymentMode = model.PaymentMode ?? "UPI";
                var effectiveUtr = model.Utr?.Trim() ?? "";
                var effectivePaidTo = model.PaidTo?.Trim() ?? "";

                if (validPayments.Count > 0)
                {
                    var sumPayments = validPayments.Sum(p => p.Amount);
                    if (sumPayments > 0) rawPayment = sumPayments;

                    var utrList = validPayments.Select(p => p.Utr?.Trim()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
                    if (utrList.Count > 0)
                    {
                        effectiveUtr = string.Join(" + ", utrList);
                    }

                    var modeList = validPayments.Select(p => p.PaymentMode?.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
                    if (modeList.Count > 0)
                    {
                        effectivePaymentMode = string.Join(" + ", modeList);
                    }

                    var paidToList = validPayments.Select(p => p.PaidTo?.Trim()).Where(pt => !string.IsNullOrWhiteSpace(pt)).Distinct().ToList();
                    if (paidToList.Count > 0)
                    {
                        effectivePaidTo = string.Join(" + ", paidToList);
                    }

                    var jsonBulkPayments = System.Text.Json.JsonSerializer.Serialize(validPayments.Select(p => new
                    {
                        date = p.Date,
                        mode = p.PaymentMode,
                        amount = p.Amount,
                        utr = p.Utr,
                        paidTo = p.PaidTo
                    }));
                    combinedNotes = $"{combinedNotes} [BULK_PAYMENTS:{jsonBulkPayments}]";
                }

                var totalFundsAvailable = rawPayment + advanceApplied;

                if (advanceApplied > 0 && matchedCredit != null && !string.IsNullOrWhiteSpace(matchedCredit.Utr))
                {
                    effectiveUtr = string.IsNullOrWhiteSpace(effectiveUtr) 
                        ? $"Adv: {matchedCredit.Utr}" 
                        : $"{effectiveUtr} + Adv: {matchedCredit.Utr}";
                }

                var masterTxn = new TransactionRecord
                {
                    Date = model.Date.ToString("yyyy-MM-dd"),
                    VehicleNo = string.Join(", ", validAllocations.Select(a => a.VehicleNo!.Trim())),
                    Imei = validAllocations[0].Imei?.Trim() ?? (firstDev?.Imei ?? ""),
                    Vendor = effVendor,
                    InvoiceId = firstDev?.InvoiceId ?? "",
                    PaymentMode = effectivePaymentMode,
                    PaymentFor = masterPaymentFor,
                    Amount = totalFundsAvailable,
                    BatchId = batchId,
                    Utr = effectiveUtr,
                    PaidTo = effectivePaidTo,
                    Notes = combinedNotes,
                    Extras = model.Extras?.Trim() ?? "None",
                    ExcludingDmg = totalExcludingDmg,
                    SimCost = totalSim,
                    CommissionCost = totalComm,
                    AdminCost = totalAdmin,
                    Profit = totalProfit,
                    Branch = "RTO"
                };
                data.Transactions.Add(masterTxn);

                // Excess advance calculation: if total funds available exceed total allocated, save remaining as new advance
                if (totalFundsAvailable > totalAllocated)
                {
                    var excess = totalFundsAvailable - totalAllocated;
                    var newCredit = new VendorCreditRecord
                    {
                        Vendor = effVendor,
                        Amount = excess,
                        RemainingBalance = excess,
                        Date = model.Date.ToString("yyyy-MM-dd"),
                        PaymentMode = effectivePaymentMode,
                        PaidTo = effectivePaidTo,
                        Utr = effectiveUtr,
                        SourceTxnId = masterTxn.Id,
                        Status = "Active",
                        Notes = $"Advance balance of ₹{excess:N2} remaining from Bulk Batch {batchId}",
                        Branch = "RTO"
                    };
                    data.VendorCredits ??= new List<VendorCreditRecord>();
                    data.VendorCredits.Add(newCredit);

                    masterTxn.Notes = $"{masterTxn.Notes} [ADVANCE_SAVED: ₹{excess:N2} credited to vendor balance]";
                    TempData["Success"] = $"Allocated {totalAllocated:C} across {validAllocations.Count} vehicles. Remaining balance of {excess:C} saved to vendor advance credit (UTR: {newCredit.Utr}).";
                }
                else
                {
                    TempData["Success"] = $"Allocated {totalAllocated:C} across {validAllocations.Count} vehicles" + (advanceApplied > 0 ? $" (including {advanceApplied:C} advance credit applied)." : ".");
                }















            }















            else















            {















                var baseAmount = model.Amount ?? 0m;















                var config = data.Config.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);































                var cleanVehStd = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim();
                var cleanImeiStd = (model.Imei ?? "").Trim();
                var cleanIccidStd = (model.Iccid ?? "").Trim();
                DeviceRecord? device = null;
                if (!string.IsNullOrWhiteSpace(cleanImeiStd) && !string.IsNullOrWhiteSpace(cleanIccidStd))
                {
                    device = data.Devices.FirstOrDefault(item => string.Equals((item.Imei ?? "").Trim(), cleanImeiStd, StringComparison.OrdinalIgnoreCase) && string.Equals((item.Iccid ?? "").Trim(), cleanIccidStd, StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && !string.IsNullOrWhiteSpace(cleanImeiStd))
                {
                    device = data.Devices.FirstOrDefault(item => string.Equals((item.Imei ?? "").Trim(), cleanImeiStd, StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && !string.IsNullOrWhiteSpace(cleanVehStd))
                {
                    device = data.Devices.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.VehicleNo) && string.Equals(item.VehicleNo.Replace(" ", "").Trim(), cleanVehStd.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && (!string.IsNullOrWhiteSpace(cleanImeiStd) || !string.IsNullOrWhiteSpace(cleanVehStd)))
                {
                    var crossMatch = _db.Devices.IgnoreQueryFilters().FirstOrDefault(item =>
                        (!string.IsNullOrWhiteSpace(cleanImeiStd) && !string.IsNullOrWhiteSpace(cleanIccidStd) && string.Equals((item.Imei ?? "").Trim(), cleanImeiStd, StringComparison.OrdinalIgnoreCase) && string.Equals((item.Iccid ?? "").Trim(), cleanIccidStd, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(cleanImeiStd) && string.Equals((item.Imei ?? "").Trim(), cleanImeiStd, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(cleanVehStd) && !string.IsNullOrWhiteSpace(item.VehicleNo) && string.Equals(item.VehicleNo.Replace(" ", "").Trim(), cleanVehStd.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));
                    if (crossMatch != null)
                    {
                        crossMatch.Branch = "RTO";
                        device = crossMatch;
                    }
                }



                var deviceInvoice = model.InvoiceId?.Trim() ?? device?.InvoiceId ?? "";















                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, deviceInvoice, StringComparison.OrdinalIgnoreCase));































                var isActivation = model.PaymentFor == "Activation" || model.PaymentFor == "NEW DEVICE" || model.PaymentFor == "Device";

                var isRecharge = string.Equals(model.PaymentFor, "Recharge", StringComparison.OrdinalIgnoreCase) || string.Equals(model.PaymentFor, "Renewal", StringComparison.OrdinalIgnoreCase) || (model.PaymentFor ?? "").Contains("Recharge", StringComparison.OrdinalIgnoreCase) || (model.PaymentFor ?? "").Contains("Renewal", StringComparison.OrdinalIgnoreCase);

                var isReactivation = string.Equals(model.PaymentFor, "Reactivation", StringComparison.OrdinalIgnoreCase);



                var adminCost = 0m;

                if (isActivation)

                {

                    if (device != null && device.AdminCost > 0m)

                    {

                        adminCost = device.AdminCost;

                    }

                    else if (config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc))

                    {

                        adminCost = parsedAc;

                        if (device != null && device.AdminCost == 0m && adminCost > 0m)

                        {

                            device.AdminCost = adminCost;

                        }

                    }

                }

                else

                {

                    if (config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc))

                    {

                        adminCost = parsedAc;

                    }

                }



                var devOp = !string.IsNullOrWhiteSpace(model.NetworkOperator) ? model.NetworkOperator : (device?.NetworkOperator ?? "");

                var deviceCost = 0m;

                if (model.IsManualDeviceCost && model.ManualDeviceCost.HasValue && model.ManualDeviceCost.Value >= 0)

                {

                    deviceCost = model.ManualDeviceCost.Value;

                }

                else if (isActivation)

                {

                    deviceCost = invoice?.CostPerDevice ?? device?.UnitCost ?? 0m;

                }

                else if (isReactivation)

                {

                    deviceCost = config.TryGetValue("PRICE_REACTIVATION", out var reaStr) && decimal.TryParse(reaStr, out var reaVal) ? reaVal : 1500m;

                }

                else if (isRecharge)

                {

                    deviceCost = config.TryGetValue("PRICE_RECHARGE", out var prStr) && decimal.TryParse(prStr, out var prVal) ? prVal : 1500m;

                }

                else

                {

                    deviceCost = 0m;

                }



                var simCost = 0m;

                var commissionCost = 0m;

                var branchType = model.Type == "Old" ? "Old" : "New";

                var simKey = (branchType == "Old") ? "SIM_COST_1_YEAR" : "SIM_COST";

                if (config.TryGetValue(simKey, out var sCost) && decimal.TryParse(sCost, out var parsedSCost)) simCost = parsedSCost;

                else simCost = (branchType == "Old") ? 1180.00m : 2360.00m;



                if (config.TryGetValue("COMMISSION_COST", out var cCost) && decimal.TryParse(cCost, out var parsedCCost)) commissionCost = parsedCCost;

                else commissionCost = 4770.00m;



                var extrasDeduction = (model.ExtrasAmount ?? 0m);

                var amount = baseAmount;

                var excludingDmg = Math.Max(0m, baseAmount - extrasDeduction);



                // For RTO:

                // Activation: deviceCost + simCost + commissionCost + adminCost

                // Recharge / Reactivation: simCost + commissionCost + adminCost

                var profit = excludingDmg - (deviceCost + simCost + commissionCost + adminCost);



                if (string.IsNullOrWhiteSpace(model.NewExpiryDate) && !string.IsNullOrWhiteSpace(model.ActivationDate))

                {

                    model.NewExpiryDate = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate, "RTO", model.Type, config) ?? "";

                }



                var transaction = new TransactionRecord

                {

                    Date = model.Date.ToString("yyyy-MM-dd"),

                    Type = model.Type,

                    VehicleNo = (model.VehicleNo ?? "").Trim(),

                    Imei = !string.IsNullOrWhiteSpace(model.Imei) ? model.Imei.Trim() : (device?.Imei ?? ""),

                    Vendor = model.Vendor?.Trim() ?? "",

                    InvoiceId = model.InvoiceId?.Trim() ?? "",

                    PaymentMode = model.PaymentMode ?? "UPI",

                    PaymentFor = model.PaymentFor ?? "None",

                    Amount = amount,

                    BatchId = model.BatchId?.Trim() ?? "",

                    Utr = model.Utr?.Trim() ?? "",

                    PaidTo = model.PaidTo?.Trim() ?? "",

                    Extras = (string.IsNullOrWhiteSpace(model.Extras) || model.Extras == "0") ? "None" : model.Extras.Trim(),

                    ActivationDate = model.ActivationDate?.Trim() ?? "",

                    NewExpiryDate = model.NewExpiryDate?.Trim() ?? "",

                    Notes = model.Notes?.Trim() ?? "",

                    ExcludingDmg = excludingDmg,

                    SimCost = simCost,

                    CommissionCost = commissionCost,

                    AdminCost = adminCost,

                    Profit = profit,

                    NetworkOperator = devOp,

                    IsManualDeviceCost = model.IsManualDeviceCost,

                    ManualDeviceCost = model.ManualDeviceCost

                };



                data.Transactions.Add(transaction);



                if (device is not null)

                {

                    var assignedVeh = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim();

                    if (string.IsNullOrWhiteSpace(device.VehicleNo) && !string.IsNullOrWhiteSpace(assignedVeh)) device.VehicleNo = assignedVeh;

                    if (device.LifecycleStatus == "Inventory" || string.IsNullOrWhiteSpace(device.LifecycleStatus)) { device.LifecycleStatus = "Active"; device.Status = "Active"; }

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















    [HttpGet]















    [RequireModulePermission("Transactions", PermissionLevel.Edit)]















    public async Task<IActionResult> Edit(string id, string? rechargeId = null)















    {















        var data = await _store.SnapshotAsync();















        var item = data.Transactions.FirstOrDefault(t => t.Id == id);















        if (item is null) return NotFound();















        var matchingDevice = data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo, item.VehicleNo, StringComparison.OrdinalIgnoreCase));















        ViewBag.DeviceLifecycle = matchingDevice?.LifecycleStatus ?? matchingDevice?.Status ?? "";

        ViewBag.KnownVendors = data.Devices

            .Where(d => !string.IsNullOrWhiteSpace(d.Vendor))

            .Select(d => d.Vendor!.Trim())

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .OrderBy(v => v)

            .ToList();















        var invoiceId = !string.IsNullOrWhiteSpace(item.InvoiceId) ? item.InvoiceId : (matchingDevice?.InvoiceId ?? "");































        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);















        ViewBag.PriceRecharge = config.TryGetValue("PRICE_RECHARGE", out var pr) ? pr : "1298";







        ViewBag.CompanyRechargeRate = config.TryGetValue("COMPANY_RECHARGE_RATE", out var cr) ? cr : "1298";







        ViewBag.CompanyReactRate = config.TryGetValue("COMPANY_REACTIVATION_RATE", out var cra) ? cra : "1534";







        ViewBag.BluemineRechargeRate = config.TryGetValue("BLUEMINE_RECHARGE_RATE", out var br) ? br : "1156.40";







        ViewBag.BluemineReactRate = config.TryGetValue("BLUEMINE_REACTIVATION_RATE", out var bra) ? bra : "1392.40";







        ViewBag.SmtRechargeRate = config.TryGetValue("SMT_RECHARGE_RATE", out var smtr) ? smtr : "1298";







        ViewBag.SmtReactRate = config.TryGetValue("SMT_REACTIVATION_RATE", out var smtra) ? smtra : "1534";







        ViewBag.SensoriseRechargeRate = config.TryGetValue("SENSORISE_RECHARGE_RATE", out var senr) ? senr : "1298";







        ViewBag.SensoriseReactRate = config.TryGetValue("SENSORISE_REACTIVATION_RATE", out var senra) ? senra : "1534";















        ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";















        ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";















        ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";















        ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";















        ViewBag.AdminCost = config.TryGetValue("ADMIN_COST", out var ac) ? ac : "0";

        ViewBag.ActivationExpiry1YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_MONTHS", out var m1) ? m1 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_1_YEAR", out var m1b) ? m1b : "12");

        ViewBag.ActivationExpiry1YearDays = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_DAYS", out var d1) ? d1 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_1_YEAR", out var d1b) ? d1b : "0");

        ViewBag.ActivationExpiry2YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_MONTHS", out var m2) ? m2 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_2_YEAR", out var m2b) ? m2b : "24");

        ViewBag.ActivationExpiry2YearDays = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_DAYS", out var d2) ? d2 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_2_YEAR", out var d2b) ? d2b : "0");

        ViewBag.ActivationExpiryMonths1Year = ViewBag.ActivationExpiry1YearMonths;

        ViewBag.ActivationExpiryDays1Year = ViewBag.ActivationExpiry1YearDays;

        ViewBag.ActivationExpiryMonths2Year = ViewBag.ActivationExpiry2YearMonths;

        ViewBag.ActivationExpiryDays2Year = ViewBag.ActivationExpiry2YearDays;















        ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "11";















        ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "15";































        var itemAmount = item.Amount ?? 0m;















        var itemProfit = item.Profit ?? 0m;















        var excludingDmg = item.ExcludingDmg.HasValue && item.ExcludingDmg.Value > 0 ? item.ExcludingDmg.Value : itemAmount;















        var totalCost = excludingDmg - itemProfit;















        if (totalCost < 0) totalCost = 0;















        var savedDeviceCost = totalCost - item.SimCost - item.CommissionCost - item.AdminCost;















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















        ViewBag.SavedCost = savedDeviceCost;















        ViewBag.SavedAdminCost = item.AdminCost;







        var linkedDev = data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo, item.VehicleNo, StringComparison.OrdinalIgnoreCase));







        







        var devOp = linkedDev?.NetworkOperator ?? "";



        ViewBag.CurrentDeviceRechargeCost = config.TryGetValue("PRICE_RECHARGE", out var prStr) && decimal.TryParse(prStr, out var prVal) ? prVal : 1500m;







        ViewBag.CurrentDeviceReactivationCost = config.TryGetValue("PRICE_REACTIVATION", out var reaStr) && decimal.TryParse(reaStr, out var reaVal) ? reaVal : 1500m;















        ViewBag.SavedProfit = item.Profit;















        ViewBag.SavedDeviceCost = savedDeviceCost;































        var isActivation = string.Equals(item.PaymentFor, "Activation", StringComparison.OrdinalIgnoreCase) || string.Equals(item.PaymentFor, "NEW DEVICE", StringComparison.OrdinalIgnoreCase) || string.Equals(item.PaymentFor, "Device", StringComparison.OrdinalIgnoreCase);















        var baseAmount = item.Amount;































        var actDate = item.ActivationDate;















        var newExpDate = item.NewExpiryDate;















        var imei = !string.IsNullOrWhiteSpace(item.Imei) ? item.Imei : (data.Devices.FirstOrDefault(dev => string.Equals(dev.VehicleNo, item.VehicleNo, StringComparison.OrdinalIgnoreCase))?.Imei ?? "");































        RechargeRecord? rec = null;















        if (!string.IsNullOrWhiteSpace(rechargeId))















        {















            rec = data.Recharges.FirstOrDefault(r => r.Id == rechargeId);















        }















        else if (!string.IsNullOrWhiteSpace(item.VehicleNo))















        {















            rec = data.Recharges















                .Where(r => string.Equals(r.VehicleNo?.Trim(), item.VehicleNo.Trim(), StringComparison.OrdinalIgnoreCase))















                .OrderByDescending(r => r.RechargeDate)















                .ThenByDescending(r => r.CreatedAt)















                .FirstOrDefault();















        }































        if (rec != null)















        {















            if ((string.IsNullOrWhiteSpace(actDate) || !string.IsNullOrWhiteSpace(rechargeId)) && !string.IsNullOrWhiteSpace(rec.ActivationDate))















            {















                actDate = DateValue.Parse(rec.ActivationDate)?.ToString("yyyy-MM-dd") ?? rec.ActivationDate;















            }















            if ((string.IsNullOrWhiteSpace(newExpDate) || !string.IsNullOrWhiteSpace(rechargeId)) && !string.IsNullOrWhiteSpace(rec.ExpiryDate))















            {















                newExpDate = DateValue.Parse(rec.ExpiryDate)?.ToString("yyyy-MM-dd") ?? rec.ExpiryDate;















            }















            if (string.IsNullOrWhiteSpace(imei) && !string.IsNullOrWhiteSpace(rec.Imei))















            {















                imei = rec.Imei;















            }















        }































        

        var (cleanNotes, totalPayment, mergeParts) = TxnMergeHelper.ExtractMergeDetails(item.Notes, item.Amount, item.Utr, item.PaymentMode, item.PaidTo);

        var isMerge = mergeParts.Count > 1 || (item.Notes != null && (item.Notes.Contains("[MERGE_PARTS:") || item.Notes.Contains("[PENDING_BALANCE:") || item.Notes.Contains("[OVERPAID:")));



        var mergePaymentRows = isMerge

            ? mergeParts.Select(p => new MergePaymentRow

            {

                Date = (p.Date != "—" && !string.IsNullOrWhiteSpace(p.Date)) ? p.Date : item.Date,

                PaymentMode = (p.Mode != "—" && !string.IsNullOrWhiteSpace(p.Mode)) ? p.Mode : (item.PaymentMode ?? "UPI"),

                Amount = p.Amount,

                Utr = (p.Utr == "—" ? "" : p.Utr),

                PaidTo = (p.PaidTo == "—" ? "" : p.PaidTo)

            }).ToList()

            : new List<MergePaymentRow>

            {

                new MergePaymentRow

                {

                    Date = item.Date,

                    PaymentMode = item.PaymentMode ?? "UPI",

                    Amount = item.Amount ?? 0m,

                    Utr = item.Utr,

                    PaidTo = item.PaidTo

                }

            };



        return View(new TransactionFormViewModel

        {

            Id = item.Id,

            Date = DateValue.Parse(item.Date) ?? DateTime.UtcNow.Date,

            Type = item.Type,

            VehicleNo = item.VehicleNo,

            Vendor = item.Vendor,

            InvoiceId = invoiceId,

            PaymentMode = item.PaymentMode ?? "",

            PaymentFor = item.PaymentFor,

            Amount = baseAmount,

            TotalPayment = isMerge ? (totalPayment ?? item.Amount) : null,

            CurrentMode = isMerge ? EntryMode.MergePartPayments : EntryMode.Standard,

            MergePayments = mergePaymentRows,

            BatchId = item.BatchId,

            Utr = item.Utr,

            PaidTo = item.PaidTo,

            Extras = (string.IsNullOrWhiteSpace(item.Extras) || item.Extras == "0") ? "None" : item.Extras,

            ExtrasAmount = item.Amount.HasValue && item.ExcludingDmg.HasValue ? item.Amount.Value - item.ExcludingDmg.Value : 0m,

            ActivationDate = actDate,

            NewExpiryDate = newExpDate,

            Notes = cleanNotes,

            AdminCost = item.AdminCost,

            Imei = imei,

            IsManualDeviceCost = item.IsManualDeviceCost,

            ManualDeviceCost = item.ManualDeviceCost,

            NetworkOperator = item.NetworkOperator ?? matchingDevice?.NetworkOperator ?? ""

        });















    }































    [HttpPost]















    [RequireModulePermission("Transactions", PermissionLevel.Edit)]















    public async Task<IActionResult> Edit(TransactionFormViewModel model)

    {

        if (string.IsNullOrWhiteSpace(model.Type)) model.Type = "New";

        var snap = await _store.SnapshotAsync();

        model.VehicleNo = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim();

        var normalized = model.VehicleNo?.Replace(" ", "").Trim();

        var deviceExists = snap.Devices.Any(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), normalized, StringComparison.OrdinalIgnoreCase));

        if (!deviceExists) ModelState.AddModelError("VehicleNo", "Not available in the data");



        if (model.CurrentMode == EntryMode.MergePartPayments && (model.MergePayments?.Any(p => p.Amount > 0) == true))

        {

            model.Amount = model.MergePayments.Where(p => p.Amount > 0).Sum(p => p.Amount);

            ModelState.Remove(nameof(model.Amount));

            ModelState.Remove(nameof(model.PaymentMode));

        }



        if (!ModelState.IsValid)

        {

            var snapConfig = await _store.SnapshotAsync();

            var config = snapConfig.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

            ViewBag.PriceRecharge = config.TryGetValue("PRICE_RECHARGE", out var pr) ? pr : "1298";

            ViewBag.CompanyRechargeRate = config.TryGetValue("COMPANY_RECHARGE_RATE", out var cr) ? cr : "1298";

            ViewBag.CompanyReactRate = config.TryGetValue("COMPANY_REACTIVATION_RATE", out var cra) ? cra : "1534";

            ViewBag.BluemineRechargeRate = config.TryGetValue("BLUEMINE_RECHARGE_RATE", out var br) ? br : "1156.40";

            ViewBag.BluemineReactRate = config.TryGetValue("BLUEMINE_REACTIVATION_RATE", out var bra) ? bra : "1392.40";

            ViewBag.SmtRechargeRate = config.TryGetValue("SMT_RECHARGE_RATE", out var smtr) ? smtr : "1298";

            ViewBag.SmtReactRate = config.TryGetValue("SMT_REACTIVATION_RATE", out var smtra) ? smtra : "1534";

            ViewBag.SensoriseRechargeRate = config.TryGetValue("SENSORISE_RECHARGE_RATE", out var senr) ? senr : "1298";

            ViewBag.SensoriseReactRate = config.TryGetValue("SENSORISE_REACTIVATION_RATE", out var senra) ? senra : "1534";

            ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";

            ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";

            ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";

            ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";

            ViewBag.AdminCost = config.TryGetValue("ADMIN_COST", out var ac) ? ac : "0";

            ViewBag.KnownVendors = snapConfig.Devices

                .Where(d => !string.IsNullOrWhiteSpace(d.Vendor))

                .Select(d => d.Vendor!.Trim())

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .OrderBy(v => v)

                .ToList();

        ViewBag.ActivationExpiry1YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_MONTHS", out var m1) ? m1 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_1_YEAR", out var m1b) ? m1b : "12");

        ViewBag.ActivationExpiry1YearDays = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_DAYS", out var d1) ? d1 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_1_YEAR", out var d1b) ? d1b : "0");

        ViewBag.ActivationExpiry2YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_MONTHS", out var m2) ? m2 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_2_YEAR", out var m2b) ? m2b : "24");

        ViewBag.ActivationExpiry2YearDays = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_DAYS", out var d2) ? d2 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_2_YEAR", out var d2b) ? d2b : "0");

        ViewBag.ActivationExpiryMonths1Year = ViewBag.ActivationExpiry1YearMonths;

        ViewBag.ActivationExpiryDays1Year = ViewBag.ActivationExpiry1YearDays;

        ViewBag.ActivationExpiryMonths2Year = ViewBag.ActivationExpiry2YearMonths;

        ViewBag.ActivationExpiryDays2Year = ViewBag.ActivationExpiry2YearDays;

            return View(model);

        }



        await _store.UpdateAsync(data =>

        {

            var item = data.Transactions.FirstOrDefault(t => t.Id == model.Id);

            if (item is null) return;



            var baseAmount = model.Amount ?? 0m;

            var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

            var currentBranch = "RTO"; // RTO area always uses RTO branch for calculations



            if (string.IsNullOrWhiteSpace(model.NewExpiryDate) && !string.IsNullOrWhiteSpace(model.ActivationDate))

            {

                model.NewExpiryDate = DeviceLifecycleHelper.CalculateExpiryFromActivation(model.ActivationDate, currentBranch, model.Type, config) ?? "";

            }



            item.VehicleNo = (model.VehicleNo ?? "").Trim();

            item.Imei = !string.IsNullOrWhiteSpace(model.Imei) ? model.Imei.Trim() : (data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo, item.VehicleNo, StringComparison.OrdinalIgnoreCase))?.Imei ?? item.Imei);

            item.Vendor = model.Vendor?.Trim() ?? "";

            item.InvoiceId = model.InvoiceId?.Trim() ?? "";

            item.PaymentFor = model.PaymentFor;

            item.Extras = (string.IsNullOrWhiteSpace(model.Extras) || model.Extras == "0") ? "None" : model.Extras.Trim();

            item.ActivationDate = model.ActivationDate?.Trim() ?? "";

            item.NewExpiryDate = model.NewExpiryDate?.Trim() ?? "";



            var dev = data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo, item.VehicleNo, StringComparison.OrdinalIgnoreCase));

            var isActivation = item.PaymentFor == "Activation" || item.PaymentFor == "NEW DEVICE" || item.PaymentFor == "Device";

            var adminCost = item.AdminCost;

            if (isActivation && adminCost == 0m)

            {

                if (dev != null && dev.AdminCost > 0m)

                {

                    adminCost = dev.AdminCost;

                }

                else if (model.AdminCost.HasValue && model.AdminCost.Value > 0m)

                {

                    adminCost = model.AdminCost.Value;

                }

                else if (config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc))

                {

                    adminCost = parsedAc;

                }

                if (dev != null && dev.AdminCost == 0m && adminCost > 0m)

                {

                    dev.AdminCost = adminCost;

                }

            }

            else if (!isActivation)

            {

                adminCost = 0m;

            }

            item.AdminCost = adminCost;



            decimal totalAmount = 0m;

            var cleanVeh = DeviceLifecycleHelper.ParseVehicleSlash(model.VehicleNo).VehicleNo.Trim();

            var existingReminder = data.ManualReminders.FirstOrDefault(r => !r.IsComplete && (r.Title.Contains(cleanVeh) || (r.Details != null && r.Details.Contains(cleanVeh))));



            if (model.CurrentMode == EntryMode.MergePartPayments)

            {

                var validPayments = (model.MergePayments ?? new List<MergePaymentRow>())

                    .Where(p => p.Amount > 0)

                    .ToList();



                if (validPayments.Count == 0 && (model.Amount ?? 0m) > 0)

                {

                    validPayments.Add(new MergePaymentRow

                    {

                        Date = model.Date.ToString("yyyy-MM-dd"),

                        PaymentMode = model.PaymentMode ?? "UPI",

                        Amount = model.Amount ?? 0m,

                        Utr = model.Utr,

                        PaidTo = model.PaidTo

                    });

                }



                foreach (var p in validPayments)

                {

                    if (string.Equals(p.PaymentMode?.Trim(), "Online", StringComparison.OrdinalIgnoreCase))

                    {

                        p.PaymentMode = "Banking";

                    }

                }



                var totalReceived = validPayments.Sum(p => p.Amount);

                var combinedModes = string.Join(" + ", validPayments.Select(p => p.PaymentMode?.Trim()).Where(m => !string.IsNullOrEmpty(m)));

                var combinedUtrs = string.Join(" + ", validPayments.Select(p => p.Utr?.Trim()).Where(u => !string.IsNullOrEmpty(u)));

                var combinedPaidTo = string.Join(" / ", validPayments.Select(p => p.PaidTo?.Trim()).Where(pt => !string.IsNullOrEmpty(pt)).Distinct());

                var partsSummary = string.Join(" + ", validPayments.Select(p => $"{p.Amount:F0} ({p.PaymentMode.Trim()})"));

                var mergePartsJson = System.Text.Json.JsonSerializer.Serialize(validPayments.Select(p => new {

                    date = !string.IsNullOrWhiteSpace(p.Date) ? p.Date.Trim() : (model.Date != default ? model.Date.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd")),

                    amount = p.Amount,

                    mode = p.PaymentMode?.Trim() ?? "",

                    utr = p.Utr?.Trim() ?? "",

                    paidTo = p.PaidTo?.Trim() ?? ""

                }));



                var pendingRemainder = (model.TotalPayment.HasValue && model.TotalPayment.Value > totalReceived)

                    ? (model.TotalPayment.Value - totalReceived)

                    : 0m;



                var balanceNote = pendingRemainder > 0

                    ? $" [PENDING_BALANCE:₹{pendingRemainder:N2}]"

                    : ((model.TotalPayment.HasValue && totalReceived > model.TotalPayment.Value) ? $" [OVERPAID:₹{totalReceived - model.TotalPayment.Value:N2}]" : "");



                var (cleanUserNotes, _, _) = TxnMergeHelper.ExtractMergeDetails(model.Notes, totalReceived, combinedUtrs, combinedModes, combinedPaidTo);

                item.Notes = $"Split Payment: {partsSummary} [MERGE_PARTS:{mergePartsJson}]" + balanceNote + (string.IsNullOrWhiteSpace(cleanUserNotes) ? "" : " " + cleanUserNotes.Trim());



                var latestPartDate = validPayments.Select(p => p.Date?.Trim()).LastOrDefault(d => !string.IsNullOrWhiteSpace(d));

                item.Date = !string.IsNullOrWhiteSpace(latestPartDate) ? latestPartDate : model.Date.ToString("yyyy-MM-dd");

                item.PaymentMode = string.IsNullOrWhiteSpace(combinedModes) ? "Merged" : combinedModes;

                item.Utr = combinedUtrs;

                item.PaidTo = combinedPaidTo;

                item.BatchId = string.IsNullOrWhiteSpace(model.BatchId) ? (string.IsNullOrWhiteSpace(item.BatchId) ? $"M-{Guid.NewGuid().ToString("N").Substring(0, 8)}" : item.BatchId) : model.BatchId.Trim();



                totalAmount = totalReceived;

                item.Amount = totalAmount;



                if (pendingRemainder > 0)

                {

                    if (existingReminder != null)

                    {

                        existingReminder.Title = $"PAYMENT DUE: {cleanVeh} - BALANCE ₹{pendingRemainder:N2}";

                        existingReminder.Details = $"Customer pending payment on vehicle {cleanVeh}. Expected Total: ₹{(model.TotalPayment ?? totalReceived):N2}, Received: ₹{totalReceived:N2}, Balance Due: ₹{pendingRemainder:N2}. Vendor: {model.Vendor}. Transaction Date: {item.Date}. Remind customer to pay remaining amount.";

                        existingReminder.DueDate = item.Date;

                    }

                    else

                    {

                        data.ManualReminders.Add(new ManualReminder

                        {

                            Id = Guid.NewGuid().ToString("N"),

                            Title = $"PAYMENT DUE: {cleanVeh} - BALANCE ₹{pendingRemainder:N2}",

                            Details = $"Customer pending payment on vehicle {cleanVeh}. Expected Total: ₹{(model.TotalPayment ?? totalReceived):N2}, Received: ₹{totalReceived:N2}, Balance Due: ₹{pendingRemainder:N2}. Vendor: {model.Vendor}. Transaction Date: {item.Date}. Remind customer to pay remaining amount.",

                            DueDate = item.Date,

                            Branch = currentBranch,

                            CreatedBy = User.Identity?.Name ?? "System",

                            IsComplete = false,

                            TargetAudience = "@all",

                            CreatedAt = DateTime.UtcNow.ToString("O")

                        });

                    }

                }

                else if (existingReminder != null)

                {

                    existingReminder.IsComplete = true;

                }

            }

            else

            {

                var (cleanUserNotes, _, _) = TxnMergeHelper.ExtractMergeDetails(model.Notes, baseAmount, model.Utr, model.PaymentMode, model.PaidTo);

                item.Notes = cleanUserNotes;

                totalAmount = baseAmount;

                item.Amount = totalAmount;

                item.BatchId = model.BatchId?.Trim() ?? "";

                item.Utr = model.Utr?.Trim() ?? "";

                item.PaidTo = model.PaidTo?.Trim() ?? "";

                item.PaymentMode = model.PaymentMode;

                item.Date = model.Date.ToString("yyyy-MM-dd");



                if (existingReminder != null)

                {

                    existingReminder.IsComplete = true;

                }

            }



            var excludingDmg = totalAmount - (model.ExtrasAmount ?? 0m);

            item.ExcludingDmg = excludingDmg;



            var isRecharge = string.Equals(item.PaymentFor, "Recharge", StringComparison.OrdinalIgnoreCase) || string.Equals(item.PaymentFor, "Renewal", StringComparison.OrdinalIgnoreCase) || (item.PaymentFor ?? "").Contains("Recharge", StringComparison.OrdinalIgnoreCase) || (item.PaymentFor ?? "").Contains("Renewal", StringComparison.OrdinalIgnoreCase);

            var isReactivation = string.Equals(item.PaymentFor, "Reactivation", StringComparison.OrdinalIgnoreCase);



            var branchType = item.Type == "Old" ? "Old" : "New";

            var simKey = (branchType == "Old") ? "SIM_COST_1_YEAR" : "SIM_COST";

            if (config.TryGetValue(simKey, out var sCost) && decimal.TryParse(sCost, out var parsedSCost)) item.SimCost = parsedSCost;

            else item.SimCost = (branchType == "Old") ? 1180.00m : 2360.00m;



            if (config.TryGetValue("COMMISSION_COST", out var cCost) && decimal.TryParse(cCost, out var parsedCCost)) item.CommissionCost = parsedCCost;

            else item.CommissionCost = 4770.00m;



            var devOp = !string.IsNullOrWhiteSpace(model.NetworkOperator) ? model.NetworkOperator : (item.NetworkOperator ?? dev?.NetworkOperator ?? "");

            item.NetworkOperator = devOp;

            var deviceCost = 0m;

            if (model.IsManualDeviceCost && model.ManualDeviceCost.HasValue && model.ManualDeviceCost.Value >= 0)

            {

                deviceCost = model.ManualDeviceCost.Value;

            }

            else if (isActivation)

            {

                if (dev != null)

                {

                    var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, item.InvoiceId, StringComparison.OrdinalIgnoreCase));

                    deviceCost = invoice?.CostPerDevice ?? dev.UnitCost;

                }

            }

            else if (isReactivation)

            {

                deviceCost = config.TryGetValue("PRICE_REACTIVATION", out var reaStr) && decimal.TryParse(reaStr, out var reaVal) ? reaVal : 1500m;

            }

            else if (isRecharge)

            {

                deviceCost = config.TryGetValue("PRICE_RECHARGE", out var prStr) && decimal.TryParse(prStr, out var prVal) ? prVal : 1500m;

            }

            else

            {

                deviceCost = 0m;

            }



            item.Profit = excludingDmg - (deviceCost + item.SimCost + item.CommissionCost + adminCost);

            item.IsManualDeviceCost = model.IsManualDeviceCost;

            item.ManualDeviceCost = model.ManualDeviceCost;



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















        ViewBag.ActivationExpiryMonths = config.TryGetValue("ACTIVATION_EXPIRY_MONTHS", out var m) ? m : "11";















        ViewBag.ActivationExpiryDays = config.TryGetValue("ACTIVATION_EXPIRY_DAYS", out var d) ? d : "15";

        ViewBag.ActivationExpiry1YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_MONTHS", out var m1) ? m1 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_1_YEAR", out var m1b) ? m1b : "12");

        ViewBag.ActivationExpiry1YearDays = config.TryGetValue("ACTIVATION_EXPIRY_1_YEAR_DAYS", out var d1) ? d1 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_1_YEAR", out var d1b) ? d1b : "0");

        ViewBag.ActivationExpiry2YearMonths = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_MONTHS", out var m2) ? m2 : (config.TryGetValue("ACTIVATION_EXPIRY_MONTHS_2_YEAR", out var m2b) ? m2b : "24");

        ViewBag.ActivationExpiry2YearDays = config.TryGetValue("ACTIVATION_EXPIRY_2_YEAR_DAYS", out var d2) ? d2 : (config.TryGetValue("ACTIVATION_EXPIRY_DAYS_2_YEAR", out var d2b) ? d2b : "0");

        ViewBag.ActivationExpiryMonths1Year = ViewBag.ActivationExpiry1YearMonths;

        ViewBag.ActivationExpiryDays1Year = ViewBag.ActivationExpiry1YearDays;

        ViewBag.ActivationExpiryMonths2Year = ViewBag.ActivationExpiry2YearMonths;

        ViewBag.ActivationExpiryDays2Year = ViewBag.ActivationExpiry2YearDays;















        ViewBag.PriceRecharge = config.TryGetValue("PRICE_RECHARGE", out var pr) ? pr : "1298";







        ViewBag.CompanyRechargeRate = config.TryGetValue("COMPANY_RECHARGE_RATE", out var cr) ? cr : "1298";







        ViewBag.CompanyReactRate = config.TryGetValue("COMPANY_REACTIVATION_RATE", out var cra) ? cra : "1534";







        ViewBag.BluemineRechargeRate = config.TryGetValue("BLUEMINE_RECHARGE_RATE", out var br) ? br : "1156.40";







        ViewBag.BluemineReactRate = config.TryGetValue("BLUEMINE_REACTIVATION_RATE", out var bra) ? bra : "1392.40";







        ViewBag.SmtRechargeRate = config.TryGetValue("SMT_RECHARGE_RATE", out var smtr) ? smtr : "1298";







        ViewBag.SmtReactRate = config.TryGetValue("SMT_REACTIVATION_RATE", out var smtra) ? smtra : "1534";







        ViewBag.SensoriseRechargeRate = config.TryGetValue("SENSORISE_RECHARGE_RATE", out var senr) ? senr : "1298";







        ViewBag.SensoriseReactRate = config.TryGetValue("SENSORISE_REACTIVATION_RATE", out var senra) ? senra : "1534";















        ViewBag.PriceReactivation = config.TryGetValue("PRICE_REACTIVATION", out var a) ? a : "0";















        ViewBag.CommissionCost = config.TryGetValue("COMMISSION_COST", out var c) ? c : "0";















        ViewBag.SimCost1Year = config.TryGetValue("SIM_COST_1_YEAR", out var s1) ? s1 : "0";















        ViewBag.SimCost2Years = config.TryGetValue("SIM_COST", out var s2) ? s2 : "0";















        ViewBag.AdminCost = config.TryGetValue("ADMIN_COST", out var ac) ? ac : "0";















        ViewBag.CostPerDevice = config.TryGetValue("PRICE_NEW_DEVICE", out var cpd) ? cpd : "0";































        var deviceLifecycleMap = data.Devices















            .Where(dev => !string.IsNullOrWhiteSpace(dev.VehicleNo))















            .GroupBy(dev => dev.VehicleNo.Trim(), StringComparer.OrdinalIgnoreCase)















            .ToDictionary(















                g => g.Key, 















                g => !string.IsNullOrWhiteSpace(g.First().LifecycleStatus) ? g.First().LifecycleStatus! : (!string.IsNullOrWhiteSpace(g.First().Status) ? g.First().Status! : "Active"), 















                StringComparer.OrdinalIgnoreCase);















        ViewBag.DeviceLifecycleMap = deviceLifecycleMap;
        ViewBag.VehicleImeiMap = data.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && !string.IsNullOrWhiteSpace(d.Imei))
            .GroupBy(d => d.VehicleNo.Replace(" ", "").Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.IsNotInUse ? 1 : 0).ThenByDescending(d => d.CreatedAt).First().Imei, StringComparer.OrdinalIgnoreCase);































        var deviceCostMap = data.Devices















            .Where(dev => !string.IsNullOrWhiteSpace(dev.VehicleNo))















            .GroupBy(dev => dev.VehicleNo.Trim(), StringComparer.OrdinalIgnoreCase)















            .ToDictionary(















                g => g.Key,















                g => {















                    var dev = g.First();















                    var inv = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, dev.InvoiceId, StringComparison.OrdinalIgnoreCase));















                    return inv != null && inv.CostPerDevice > 0 ? inv.CostPerDevice : dev.UnitCost;















                },















                StringComparer.OrdinalIgnoreCase);















        ViewBag.DeviceCostMap = deviceCostMap;































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















    public async Task<IActionResult> BulkSplit([FromQuery] string? deviceIds = null, [FromQuery] string? vehicles = null)















    {















        var data = await _store.SnapshotAsync();















        var branch = "RTO";















        await PopulateBulkSplitViewBagAsync(data, branch);































        var model = new BulkSplitViewModel();































        var matchedDevices = new List<DeviceRecord>();















        if (!string.IsNullOrWhiteSpace(deviceIds))















        {















            var idSet = deviceIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)















                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);















            matchedDevices = data.Devices.Where(d => idSet.Contains(d.Id)).ToList();

            foreach (var md in matchedDevices)

            {

                if (string.IsNullOrWhiteSpace(md.VehicleNo))

                {

                    md.VehicleNo = !string.IsNullOrWhiteSpace(md.Imei) ? md.Imei : ("DEV-" + md.Id);

                }

            }















        }















        else if (!string.IsNullOrWhiteSpace(vehicles))















        {















            var vehList = vehicles.Split(new[] { ',', '\n', '\r', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);















            foreach (var v in vehList)















            {















                var cleanV = v.Trim();















                var dev = data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo, cleanV, StringComparison.OrdinalIgnoreCase));















                if (dev != null)















                {















                    matchedDevices.Add(dev);















                }















                else















                {















                    matchedDevices.Add(new DeviceRecord { VehicleNo = cleanV });















                }















            }















        }































        if (matchedDevices.Count > 0)















        {















            var vendors = matchedDevices.Select(d => d.Vendor).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();















            if (vendors.Count == 1)















            {















                model.Vendor = vendors[0];















            }































            var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);















            model.BulkAllocations = matchedDevices.Select(d =>















            {















                var (actDate, expDate, _) = DeviceLifecycleHelper.ResolveDatesForTransaction(















                    d.VehicleNo,















                    model.Date,















                    branch,















                    data.Recharges,















                    data.Devices,















                    config, model.PaymentFor, d.Imei);































                return new BulkAllocationRow















                {















                    VehicleNo = d.VehicleNo,
                    Imei = d.Imei,















                    ActivationDate = actDate,















                    NewExpiryDate = expDate,















                    Extras = "None",















                    ExtrasAmount = 0















                };















            }).ToList();































            ViewBag.SelectedFromDevicesCount = matchedDevices.Count;















        }































        return View("BulkSplit", model);















    }































    [HttpPost]















    [RequireModulePermission("Transactions", PermissionLevel.Edit)]















    public async Task<IActionResult> BulkSplitFromSelection([FromForm] string? selectedIds = null, [FromForm] string? selectedVehicles = null)















    {















        return await BulkSplit(selectedIds, selectedVehicles);















    }































    [HttpPost]















    [RequireModulePermission("Transactions", PermissionLevel.Edit)]















    public async Task<IActionResult> BulkSplit(BulkSplitViewModel model)















    {















        var data = await _store.SnapshotAsync();















        var branch = "RTO";































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































            var paymentFor = string.IsNullOrWhiteSpace(model.PaymentFor) ? "None" : model.PaymentFor.Trim();















            var isActivation = string.Equals(paymentFor, "Activation", StringComparison.OrdinalIgnoreCase) || string.Equals(paymentFor, "Device", StringComparison.OrdinalIgnoreCase);















            var isRecharge = string.Equals(paymentFor, "Recharge", StringComparison.OrdinalIgnoreCase);















            var isReactivation = string.Equals(paymentFor, "Reactivation", StringComparison.OrdinalIgnoreCase);































            var defaultRechargeCost = 0m;















            if (config.TryGetValue("PRICE_RECHARGE", out var prStr) && decimal.TryParse(prStr, out var parsedPr)) defaultRechargeCost = parsedPr;















            var defaultReactivationCost = 0m;















            if (config.TryGetValue("PRICE_REACTIVATION", out var praStr) && decimal.TryParse(praStr, out var parsedPra)) defaultReactivationCost = parsedPra;































            var configAdminCost = 0m;















            if (isActivation && config.TryGetValue("ADMIN_COST", out var acStr) && decimal.TryParse(acStr, out var parsedAc)) configAdminCost = parsedAc;































            
            var masterPaymentFor = !string.IsNullOrWhiteSpace(model.PaymentFor) && !model.PaymentFor.Equals("Bulk allocation", StringComparison.OrdinalIgnoreCase) && !model.PaymentFor.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? model.PaymentFor.Trim()
                : (branch == "RTO" ? "NEW DEVICE" : "Activation");

            var bulkPartsList = new List<object>();
            decimal totalProfit = 0m;
            decimal totalExcludingDmg = 0m;
            decimal totalSim = 0m;
            decimal totalComm = 0m;
            decimal totalAdmin = 0m;

            foreach (var allocation in validAllocations)
            {
                var excludingDmg = allocation.Amount - allocation.ExtrasAmount;
                if (string.IsNullOrWhiteSpace(allocation.NewExpiryDate) || string.IsNullOrWhiteSpace(allocation.ActivationDate))
                {
                    var (resAct, resExp, _) = DeviceLifecycleHelper.ResolveDatesForTransaction(
                        allocation.VehicleNo,
                        model.Date,
                        branch,
                        data.Recharges,
                        data.Devices,
                        config);
                    if (string.IsNullOrWhiteSpace(allocation.ActivationDate)) allocation.ActivationDate = resAct;
                    if (string.IsNullOrWhiteSpace(allocation.NewExpiryDate)) allocation.NewExpiryDate = resExp;
                }

                var cleanVeh = (allocation.VehicleNo ?? "").Replace(" ", "").Trim();
                var cleanAllocImei = (allocation.Imei ?? "").Trim();
                var cleanAllocIccid = (allocation.Iccid ?? "").Trim();
                DeviceRecord? device = null;
                if (!string.IsNullOrWhiteSpace(cleanAllocImei) && !string.IsNullOrWhiteSpace(cleanAllocIccid))
                {
                    device = data.Devices.FirstOrDefault(item => string.Equals((item.Imei ?? "").Trim(), cleanAllocImei, StringComparison.OrdinalIgnoreCase) && string.Equals((item.Iccid ?? "").Trim(), cleanAllocIccid, StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && !string.IsNullOrWhiteSpace(cleanAllocImei))
                {
                    device = data.Devices.FirstOrDefault(item => string.Equals((item.Imei ?? "").Trim(), cleanAllocImei, StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && !string.IsNullOrWhiteSpace(cleanVeh))
                {
                    device = data.Devices.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.VehicleNo) && item.VehicleNo.Replace(" ", "").Equals(cleanVeh, StringComparison.OrdinalIgnoreCase));
                }
                if (device == null && (!string.IsNullOrWhiteSpace(cleanAllocImei) || !string.IsNullOrWhiteSpace(cleanVeh)))
                {
                    var crossMatch = _db.Devices.IgnoreQueryFilters().FirstOrDefault(item =>
                        (!string.IsNullOrWhiteSpace(cleanAllocImei) && !string.IsNullOrWhiteSpace(cleanAllocIccid) && string.Equals((item.Imei ?? "").Trim(), cleanAllocImei, StringComparison.OrdinalIgnoreCase) && string.Equals((item.Iccid ?? "").Trim(), cleanAllocIccid, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(cleanAllocImei) && string.Equals((item.Imei ?? "").Trim(), cleanAllocImei, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrWhiteSpace(cleanVeh) && !string.IsNullOrWhiteSpace(item.VehicleNo) && item.VehicleNo.Replace(" ", "").Equals(cleanVeh, StringComparison.OrdinalIgnoreCase)));
                    if (crossMatch != null)
                    {
                        crossMatch.Branch = "RTO";
                        device = crossMatch;
                    }
                }

                var deviceInvoice = device?.InvoiceId ?? "";
                var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, deviceInvoice, StringComparison.OrdinalIgnoreCase));
                var deviceCost = 0m;
                if (isActivation && device != null)
                {
                    deviceCost = invoice?.CostPerDevice ?? device.UnitCost;
                }
                else if (isRecharge)
                {
                    deviceCost = defaultRechargeCost;
                }
                else if (isReactivation)
                {
                    deviceCost = defaultReactivationCost;
                }

                var adminCost = (isActivation && device != null && device.AdminCost > 0m) ? device.AdminCost : configAdminCost;
                var rowAmount = allocation.Amount;
                excludingDmg = rowAmount - allocation.ExtrasAmount;

                var profit = branch == "DMG"
                    ? (excludingDmg - deviceCost - adminCost)
                    : (excludingDmg - (deviceCost + simCost + commissionCost + adminCost));

                totalProfit += profit;
                totalExcludingDmg += excludingDmg;
                totalSim += simCost;
                totalComm += commissionCost;
                totalAdmin += adminCost;

                bulkPartsList.Add(new
                {
                    vehicleNo = allocation.VehicleNo!.Trim(),
                    imei = !string.IsNullOrWhiteSpace(allocation.Imei) ? allocation.Imei.Trim() : (device?.Imei ?? ""),
                    @operator = !string.IsNullOrWhiteSpace(allocation.NetworkOperator) ? allocation.NetworkOperator.Trim() : (device?.NetworkOperator ?? ""),
                    amount = rowAmount,
                    profit = profit,
                    activationDate = allocation.ActivationDate?.Trim() ?? "",
                    newExpiryDate = allocation.NewExpiryDate?.Trim() ?? ""
                });

                if (device is not null)
                {
                    device.Paid = "Yes";
                    if (!string.IsNullOrWhiteSpace(allocation.ActivationDate)) device.ActivationDate = allocation.ActivationDate.Trim();
                    if (!string.IsNullOrWhiteSpace(allocation.NewExpiryDate)) device.LatestExpiryDate = allocation.NewExpiryDate.Trim();
                }

                var matchingRec = data.Recharges
                    .Where(r => string.Equals(r.VehicleNo?.Replace(" ", "").Trim(), allocation.VehicleNo.Replace(" ", "").Trim(), StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.RechargeDate)
                    .ThenByDescending(r => r.CreatedAt)
                    .FirstOrDefault();

                if (matchingRec != null)
                {
                    matchingRec.PaymentStatus = "Paid";
                    matchingRec.RechargeStatus = "Processed";
                    if (!string.IsNullOrWhiteSpace(allocation.ActivationDate)) matchingRec.ActivationDate = allocation.ActivationDate.Trim();
                    if (!string.IsNullOrWhiteSpace(allocation.NewExpiryDate)) matchingRec.ExpiryDate = allocation.NewExpiryDate.Trim();
                }
            }

            var jsonParts = System.Text.Json.JsonSerializer.Serialize(bulkPartsList);
            var userNotes = model.Notes?.Trim() ?? "";
            var combinedNotes = string.IsNullOrWhiteSpace(userNotes)
                ? $"Bulk Allocation ({validAllocations.Count} Vehicles): {string.Join(", ", validAllocations.Select(a => a.VehicleNo!.Trim()))} [BULK_PARTS:{jsonParts}]"
                : $"{userNotes} [BULK_PARTS:{jsonParts}]";

            var validPayments = (model.BulkPayments ?? new List<MergePaymentRow>())
                .Where(p => p.Amount > 0 || !string.IsNullOrWhiteSpace(p.Utr))
                .ToList();

            var totalAllocated = validAllocations.Sum(a => a.Amount);
            var totalAmt = model.TotalPayment > 0 ? model.TotalPayment : totalAllocated;
            var effectivePaymentMode = model.PaymentMode ?? "Bank transfer";
            var effectiveUtr = model.Utr?.Trim() ?? "";
            var effectivePaidTo = model.PaidTo?.Trim() ?? "";

            if (validPayments.Count > 0)
            {
                var sumPayments = validPayments.Sum(p => p.Amount);
                if (sumPayments > 0) totalAmt = sumPayments;

                var utrList = validPayments.Select(p => p.Utr?.Trim()).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
                if (utrList.Count > 0) effectiveUtr = string.Join(" + ", utrList);

                var modeList = validPayments.Select(p => p.PaymentMode?.Trim()).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
                if (modeList.Count > 0) effectivePaymentMode = string.Join(" + ", modeList);
                else if (modeList.Count == 1) effectivePaymentMode = modeList[0];

                var paidToList = validPayments.Select(p => p.PaidTo?.Trim()).Where(pt => !string.IsNullOrWhiteSpace(pt)).Distinct().ToList();
                if (paidToList.Count > 0) effectivePaidTo = string.Join(" + ", paidToList);

                var jsonBulkPayments = System.Text.Json.JsonSerializer.Serialize(validPayments.Select(p => new
                {
                    date = p.Date,
                    mode = p.PaymentMode,
                    amount = p.Amount,
                    utr = p.Utr,
                    paidTo = p.PaidTo
                }));
                combinedNotes = $"{combinedNotes} [BULK_PAYMENTS:{jsonBulkPayments}]";
            }

            var firstDev = data.Devices.FirstOrDefault(item => string.Equals((item.VehicleNo ?? "").Replace(" ", ""), validAllocations[0].VehicleNo!.Replace(" ", "").Trim(), StringComparison.OrdinalIgnoreCase));

            var masterTxn = new TransactionRecord
            {
                Date = model.Date.ToString("yyyy-MM-dd"),
                VehicleNo = string.Join(", ", validAllocations.Select(a => a.VehicleNo!.Trim())),
                Imei = validAllocations[0].Imei?.Trim() ?? (firstDev?.Imei ?? ""),
                Vendor = model.Vendor?.Trim() ?? (firstDev?.Vendor ?? ""),
                InvoiceId = firstDev?.InvoiceId ?? "",
                PaymentMode = effectivePaymentMode,
                PaymentFor = masterPaymentFor,
                Amount = totalAmt,
                BatchId = batchId,
                Utr = effectiveUtr,
                PaidTo = effectivePaidTo,
                Notes = combinedNotes,
                Extras = model.Extras?.Trim() ?? "None",
                ExcludingDmg = totalExcludingDmg,
                SimCost = totalSim,
                CommissionCost = totalComm,
                AdminCost = totalAdmin,
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















        try {

            var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "telemetry.log");

            System.IO.File.AppendAllText(logPath, $"{DateTime.UtcNow}: {data}" + Environment.NewLine);

        } catch {}















        return Ok();















    }































    
    [HttpPost]
    [RequireModulePermission("Transactions", PermissionLevel.Edit)]
    public async Task<IActionResult> QuickUpdateExpiry([FromBody] QuickExpiryUpdateRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Id) || string.IsNullOrWhiteSpace(req.NewExpiryDate))
            return Json(new { success = false, message = "Invalid parameters." });

        var parsedDate = DateValue.Parse(req.NewExpiryDate);
        if (!parsedDate.HasValue)
            return Json(new { success = false, message = "Invalid date format." });

        var formattedDate = parsedDate.Value.ToString("yyyy-MM-dd");
        int updatedCount = 0;

        await _store.UpdateAsync(data =>
        {
            var targetTxn = data.Transactions.FirstOrDefault(t => t.Id == req.Id);
            if (targetTxn == null) return;

            var txnsToUpdate = new List<TransactionRecord>();
            if (req.ApplyToBatch && !string.IsNullOrWhiteSpace(targetTxn.BatchId) && targetTxn.BatchId != "-")
            {
                txnsToUpdate = data.Transactions.Where(t => t.BatchId == targetTxn.BatchId).ToList();
            }
            else
            {
                txnsToUpdate.Add(targetTxn);
            }

            foreach (var txn in txnsToUpdate)
            {
                txn.NewExpiryDate = formattedDate;
                updatedCount++;

                var cleanVeh = (txn.VehicleNo ?? "").Replace(" ", "").Trim();
                var cleanImei = (txn.Imei ?? "").Trim();

                var dev = (!string.IsNullOrWhiteSpace(cleanImei) ? data.Devices.FirstOrDefault(d => string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase)) : null)
                    ?? (!string.IsNullOrWhiteSpace(cleanVeh) ? data.Devices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && d.VehicleNo.Replace(" ", "").Trim().Equals(cleanVeh, StringComparison.OrdinalIgnoreCase)) : null);

                if (dev != null)
                {
                    dev.LatestExpiryDate = formattedDate;
                }

                var matchingRec = (!string.IsNullOrWhiteSpace(cleanImei) ? data.Recharges.FirstOrDefault(r => string.Equals((r.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase)) : null)
                    ?? (!string.IsNullOrWhiteSpace(cleanVeh) ? data.Recharges.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.VehicleNo) && r.VehicleNo.Replace(" ", "").Trim().Equals(cleanVeh, StringComparison.OrdinalIgnoreCase)) : null);

                if (matchingRec != null)
                {
                    matchingRec.ExpiryDate = formattedDate;
                }
            }
        });

        return Json(new { success = true, newExpiryDate = formattedDate, displayDate = DateValue.Short(formattedDate), updatedCount });
    }


[HttpGet]















    [AllowAnonymous]















    public async Task<IActionResult> GetDeviceInfo(string? vehicleNo = null, string? rechargeId = null, string? deviceId = null, string? imei = null, string? iccid = null, string? paymentFor = null)
    {
        var currentBranch = "RTO";
        if (string.IsNullOrWhiteSpace(vehicleNo) && string.IsNullOrWhiteSpace(rechargeId) && string.IsNullOrWhiteSpace(deviceId) && string.IsNullOrWhiteSpace(imei) && string.IsNullOrWhiteSpace(iccid))
            return Json(new { isFound = false, unitCost = 0, vendor = "", invoiceId = "", imei = "" });

        var normalizedQuery = (vehicleNo ?? "").Replace(" ", "").Trim();
        var dataSnapshot = await _store.SnapshotAsync();
        RechargeRecord? rec = null;
        if (!string.IsNullOrWhiteSpace(rechargeId))
        {
            rec = dataSnapshot.Recharges.FirstOrDefault(r => r.Id == rechargeId);
        }
        if (rec == null && !string.IsNullOrWhiteSpace(normalizedQuery))
        {
            rec = dataSnapshot.Recharges
                .Where(r => string.Equals(r.VehicleNo?.Replace(" ", "").Trim(), normalizedQuery, StringComparison.OrdinalIgnoreCase) && string.Equals(r.Branch, currentBranch, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.RechargeDate)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefault()
                ?? dataSnapshot.Recharges
                .Where(r => string.Equals(r.VehicleNo?.Replace(" ", "").Trim(), normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.RechargeDate)
                .ThenByDescending(r => r.CreatedAt)
                .FirstOrDefault();
        }

        var allDevices = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(_db.Devices).AsNoTracking().ToListAsync();
        DeviceRecord? device = null;
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            device = allDevices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
        }
        if (device == null && (!string.IsNullOrWhiteSpace(imei) || !string.IsNullOrWhiteSpace(iccid)))
        {
            var cleanImei = (imei ?? "").Trim();
            var cleanIccid = (iccid ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(cleanImei) && !string.IsNullOrWhiteSpace(cleanIccid))
            {
                device = allDevices.FirstOrDefault(d => string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase) && string.Equals((d.Iccid ?? "").Trim(), cleanIccid, StringComparison.OrdinalIgnoreCase) && string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase))
                    ?? allDevices.FirstOrDefault(d => string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase) && string.Equals((d.Iccid ?? "").Trim(), cleanIccid, StringComparison.OrdinalIgnoreCase));
            }
            if (device == null && !string.IsNullOrWhiteSpace(cleanImei))
            {
                device = allDevices.FirstOrDefault(d => string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase) && string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase))
                    ?? allDevices.FirstOrDefault(d => string.Equals((d.Imei ?? "").Trim(), cleanImei, StringComparison.OrdinalIgnoreCase));
            }
            if (device == null && !string.IsNullOrWhiteSpace(cleanIccid))
            {
                device = allDevices.FirstOrDefault(d => string.Equals((d.Iccid ?? "").Trim(), cleanIccid, StringComparison.OrdinalIgnoreCase) && string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase))
                    ?? allDevices.FirstOrDefault(d => string.Equals((d.Iccid ?? "").Trim(), cleanIccid, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (device == null && !string.IsNullOrWhiteSpace(normalizedQuery))
        {
            device = allDevices
                .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), normalizedQuery, StringComparison.OrdinalIgnoreCase) && string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.IsNotInUse ? 1 : 0)
                .ThenByDescending(d => d.CreatedAt)
                .FirstOrDefault()
                ?? allDevices
                .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.IsNotInUse ? 1 : 0)
                .ThenByDescending(d => d.CreatedAt)
                .FirstOrDefault();
        }
        if (device == null && rec != null)
        {
            var cleanRecVeh = rec.VehicleNo?.Replace(" ", "").Trim();
            if (!string.IsNullOrWhiteSpace(cleanRecVeh))
            {
                device = allDevices
                    .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), cleanRecVeh, StringComparison.OrdinalIgnoreCase) && string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.IsNotInUse ? 1 : 0)
                    .ThenByDescending(d => d.CreatedAt)
                    .FirstOrDefault()
                    ?? allDevices
                    .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo) && string.Equals(d.VehicleNo.Replace(" ", "").Trim(), cleanRecVeh, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.IsNotInUse ? 1 : 0)
                    .ThenByDescending(d => d.CreatedAt)
                    .FirstOrDefault();
            }
        }



        if (device is null && rec is null)

            return Json(new { isFound = false, unitCost = 0, vendor = "", invoiceId = "", imei = "" });



        var allInvoices = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(_db.Invoices).AsNoTracking().ToListAsync();

        var invoice = device != null && !string.IsNullOrWhiteSpace(device.InvoiceId)

            ? (allInvoices.FirstOrDefault(i => string.Equals(i.InvoiceNo?.Trim(), device.InvoiceId.Trim(), StringComparison.OrdinalIgnoreCase) && string.Equals(i.Branch, currentBranch, StringComparison.OrdinalIgnoreCase))

               ?? allInvoices.FirstOrDefault(i => string.Equals(i.InvoiceNo?.Trim(), device.InvoiceId.Trim(), StringComparison.OrdinalIgnoreCase)))

            : null;



        var unitCost = invoice != null && invoice.CostPerDevice > 0 

            ? invoice.CostPerDevice 

            : (invoice != null && invoice.Quantity > 0 ? (invoice.TotalWithTaxes / invoice.Quantity) : (device?.UnitCost ?? 0m));



        var dbConfigs = await _db.Config.IgnoreQueryFilters().Where(c => c.Branch == currentBranch).AsNoTracking().ToListAsync();

        var configMap = dbConfigs.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

        var branchDefaults = currentBranch == "RTO" ? DeviceLifecycleHelper.RtoDefaults : DeviceLifecycleHelper.DmgDefaults;

        foreach (var kvp in branchDefaults) { if (!configMap.ContainsKey(kvp.Key)) configMap[kvp.Key] = kvp.Value; }



        var normalizedType = device?.Type;

        if (!string.IsNullOrWhiteSpace(normalizedType))

        {

            if (normalizedType.Contains("2", StringComparison.OrdinalIgnoreCase) || normalizedType.Equals("New", StringComparison.OrdinalIgnoreCase))

                normalizedType = "New";

            else if (normalizedType.Contains("1", StringComparison.OrdinalIgnoreCase) || normalizedType.Equals("Old", StringComparison.OrdinalIgnoreCase))

                normalizedType = "Old";

        }



        var (resolvedAct, resolvedExp, fromRecharge) = DeviceLifecycleHelper.ResolveDatesForTransaction(

            !string.IsNullOrWhiteSpace(normalizedQuery) ? normalizedQuery : (device?.VehicleNo ?? rec?.VehicleNo),

            DateTime.UtcNow,

            currentBranch,

            dataSnapshot.Recharges,

            allDevices,

            configMap, paymentFor, device?.Imei ?? imei);



        var parsedActivation = resolvedAct;

        var parsedExpiry = resolvedExp;

        var calculatedExpiry = resolvedExp;



        var devOp = device?.NetworkOperator ?? rec?.NetworkOperator ?? "";

        var resolvedRechargeCost = decimal.TryParse(configMap.GetValueOrDefault("PRICE_RECHARGE"), out var prc) ? prc : 1500.00m;

        var resolvedReactCost = decimal.TryParse(configMap.GetValueOrDefault("PRICE_REACTIVATION"), out var prAct) ? prAct : 1500.00m;



        decimal simCost = (normalizedType == "Old")

            ? (decimal.TryParse(configMap.GetValueOrDefault("SIM_COST_1_YEAR"), out var s1) ? s1 : 1180.00m)

            : (decimal.TryParse(configMap.GetValueOrDefault("SIM_COST_2_YEARS"), out var s2) ? s2 : 2360.00m);

        decimal commissionCost = decimal.TryParse(configMap.GetValueOrDefault("COMMISSION_COST"), out var cc) ? cc : 4770.00m;

        decimal rtoCombinedCost = unitCost + simCost + commissionCost;



        return Json(new { 

            isFound = true, 

            fromRecharge = fromRecharge, 

            unitCost = unitCost, 

            simCost = simCost,

            commissionCost = commissionCost,

            rtoCombinedCost = rtoCombinedCost,

            adminCost = device?.AdminCost,

            vendor = device?.Vendor ?? rec?.Vendor ?? "", 

            invoiceId = device?.InvoiceId ?? "", 

            imei = device?.Imei ?? rec?.Imei ?? "",

            type = normalizedType ?? "",

            rawType = device?.Type ?? "",

            activationDate = parsedActivation ?? "",

            latestExpiryDate = device?.LatestExpiryDate ?? rec?.ExpiryDate ?? "",

            prevExpiryDate = device?.LatestExpiryDate ?? rec?.ExpiryDate ?? "",

            calculatedExpiry = calculatedExpiry ?? "",

            networkOperator = !string.IsNullOrWhiteSpace(devOp) ? devOp : "Taisys (MYGTS)",

            rechargeCost = resolvedRechargeCost,

            reactivationCost = resolvedReactCost,

            status = device != null ? (!string.IsNullOrWhiteSpace(device.LifecycleStatus) ? device.LifecycleStatus : (device.Status ?? "")) : (rec?.PaymentStatus ?? "")

        });

    }



    public async Task<IActionResult> GetInvoiceCost(string invoiceId)















    {















        if (string.IsNullOrWhiteSpace(invoiceId)) return Json(new { unitCost = 0 });















        var data = await _store.SnapshotAsync();















        var invoice = data.Invoices.FirstOrDefault(i => string.Equals(i.InvoiceNo, invoiceId.Trim(), StringComparison.OrdinalIgnoreCase));















        return Json(new { unitCost = invoice?.CostPerDevice ?? 0 });















    }

    [HttpGet]
    public async Task<IActionResult> GetVendorCredit([FromQuery] string vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor)) return Json(new { hasCredit = false });
        var data = await _store.SnapshotAsync();
        var cleanVen = vendor.Trim();
        var credit = (data.VendorCredits ?? new List<VendorCreditRecord>())
            .Where(c => string.Equals((c.Vendor ?? "").Trim(), cleanVen, StringComparison.OrdinalIgnoreCase) && c.RemainingBalance > 0 && c.Status == "Active")
            .OrderByDescending(c => c.Date)
            .ThenByDescending(c => c.CreatedAt)
            .FirstOrDefault();

        if (credit == null) return Json(new { hasCredit = false });

        return Json(new
        {
            hasCredit = true,
            creditId = credit.Id,
            amount = credit.Amount,
            remainingBalance = credit.RemainingBalance,
            date = credit.Date,
            paymentMode = credit.PaymentMode,
            paidTo = credit.PaidTo,
            utr = credit.Utr,
            notes = credit.Notes
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetVendorCreditsList()
    {
        var data = await _store.SnapshotAsync();
        var list = (data.VendorCredits ?? new List<VendorCreditRecord>())
            .OrderByDescending(c => c.Status == "Active")
            .ThenByDescending(c => c.Date)
            .ThenByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                id = c.Id,
                vendor = c.Vendor,
                amount = c.Amount,
                remainingBalance = c.RemainingBalance,
                date = c.Date,
                paymentMode = c.PaymentMode,
                paidTo = c.PaidTo,
                utr = c.Utr,
                status = c.Status,
                sourceTxnId = c.SourceTxnId,
                notes = c.Notes,
                createdAt = c.CreatedAt
            })
            .ToList();

        var totalActive = list.Where(c => c.status == "Active").Sum(c => c.remainingBalance);

        return Json(new
        {
            totalActiveBalance = totalActive,
            credits = list
        });
    }































    [HttpGet("~/RTO/Transactions/ExportAll")]
    [HttpGet("~/Transactions/ExportAll")]















    public async Task<IActionResult> ExportAll([FromQuery] TransactionFilterViewModel filters, [FromQuery] string? branch = null)















    {















        var data = await _store.SnapshotAsync();















        var activeBranch = !string.IsNullOrWhiteSpace(branch) ? branch : "RTO";















        var sortedTransactions = FilterAndSortTransactions(data, filters);















        var bytes = _spreadsheets.ExportTransactions(sortedTransactions, activeBranch);















        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"mygts-transactions-{activeBranch.ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd}.xlsx");















    }































    [HttpGet("~/Transactions/DownloadSample")]















    [AllowAnonymous]















    public IActionResult DownloadSample([FromQuery] string? branch = null)















    {















        var activeBranch = !string.IsNullOrWhiteSpace(branch) ? branch : "RTO";















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















                ["1", "2026-09-01", "KA01AB1234", "Sample Vendor", "UPI", "Recharge", "1500.00", "BATCH-DMG-01", "UTR123456789", "MYGTS", "None", "2026-09-01", "2027-08-31", "300.00", "Sample DMG payment notes"],















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















        var currentBranch = "RTO";















        ViewBag.CurrentBranch = currentBranch;















        return View(new ImportViewModel());















    }































    [HttpPost]















    [RequireModulePermission("Transactions", PermissionLevel.Edit)]















    public async Task<IActionResult> Import(ImportViewModel model)















    {















        var currentBranch = "RTO";















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































        var currentBranch = !string.IsNullOrWhiteSpace(model.Branch) ? model.Branch : "RTO";































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















        var currentBranch = "RTO"; // RTO area always uses RTO branch for calculations















        int updatedCount = 0;































        await _store.UpdateAsync(data =>















        {















            var config = data.Config.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);































            decimal simCost2Year = 0m;















            decimal simCost1Year = 0m;















            decimal commissionCost = 0m;















            decimal rechargePrice = 0m;















            decimal reactivationPrice = 0m;































            decimal adminCostConfig = 0m;















            if (config.TryGetValue("ADMIN_COST", out var ac) && decimal.TryParse(ac, out var pac)) adminCostConfig = pac;































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















                var isActivation = paymentFor.Equals("Activation", StringComparison.OrdinalIgnoreCase) ||















                    paymentFor.Equals("NEW DEVICE", StringComparison.OrdinalIgnoreCase) ||















                    paymentFor.Equals("Device", StringComparison.OrdinalIgnoreCase);































                if (isActivation)















                {















                    deviceCost = inv?.CostPerDevice ?? dev?.UnitCost ?? 0m;















                }















                else if (paymentFor.Equals("Reactivation", StringComparison.OrdinalIgnoreCase))

                {

                    deviceCost = config.TryGetValue("PRICE_REACTIVATION", out var reaStr) && decimal.TryParse(reaStr, out var reaVal) ? reaVal : 1500m;

                }

                else if (paymentFor.Equals("Recharge", StringComparison.OrdinalIgnoreCase) || paymentFor.Equals("Renewal", StringComparison.OrdinalIgnoreCase))

                {

                    deviceCost = config.TryGetValue("PRICE_RECHARGE", out var prStr) && decimal.TryParse(prStr, out var prVal) ? prVal : 1500m;

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































                if (isActivation && txn.AdminCost == 0m)















                {















                    if (dev != null && dev.AdminCost > 0m)















                    {















                        txn.AdminCost = dev.AdminCost;















                    }















                    else if (adminCostConfig > 0m)















                    {















                        txn.AdminCost = adminCostConfig;















                    }















                }















                if (dev != null && dev.AdminCost == 0m && txn.AdminCost > 0m)















                {















                    dev.AdminCost = txn.AdminCost;















                }















                var applicableAdminCost = txn.AdminCost;































                var amount = txn.Amount ?? 0m;















                var excludingDmg = txn.ExcludingDmg ?? amount;















                txn.ExcludingDmg = excludingDmg;































                if (currentBranch == "DMG")















                {















                    txn.Profit = isActivation















                        ? excludingDmg - (deviceCost + applicableAdminCost)















                        : excludingDmg - deviceCost;















                }















                else















                {















                    txn.Profit = isActivation















                        ? excludingDmg - (deviceCost + applicableSimCost + applicableCommCost + applicableAdminCost)















                        : excludingDmg - (deviceCost + applicableSimCost + applicableCommCost);















                }















                updatedCount++;















            }















        });































        TempData["Success"] = $"Recalculated {updatedCount} transactions for {currentBranch} branch based on current configuration.";















        return RedirectToAction(nameof(Index));















    }















}















