using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using MygtsMvc.Data;

using MygtsMvc.Models;

using MygtsMvc.Services;

using MygtsMvc.ViewModels;

using System.Text;



namespace MygtsMvc.Areas.RTO.Controllers;

using Microsoft.AspNetCore.Mvc;



[Area("RTO")]

[Authorize]

[RequireModulePermission("Recharges", PermissionLevel.View)]

public sealed class RechargesController : Controller

{

    private readonly IAppDataStore _store;



    public RechargesController(IAppDataStore store)

    {

        _store = store;

    }



    [HttpGet]

    public async Task<IActionResult> Index()

    {

        var data = await _store.SnapshotAsync();

        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";



        // Audit processed unpaid recharges against transactions by ICCID/IMEI/VehicleNo

        var auditChanges = RechargeAuditHelper.AuditProcessedRechargePayments(data, branch);

        if (auditChanges > 0)

        {

            await _store.UpdateAsync(d => RechargeAuditHelper.AuditProcessedRechargePayments(d, branch));

            data = await _store.SnapshotAsync();

        }



        var rawRecharges = await _store.GetRechargesAsync();

        var recharges = RechargeAuditHelper.FilterDuplicates(rawRecharges);



        var outstanding = recharges

            .Where(r => string.Equals(r.RechargeStatus, "Processed", StringComparison.OrdinalIgnoreCase)

                && !string.IsNullOrWhiteSpace(r.VehicleNo)

                && !string.IsNullOrWhiteSpace(r.RechargeDate))

            .Where(r => !string.Equals(r.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))

            .Select(r => new RechargeAnalyticsItem

            {

                VehicleNo = r.VehicleNo,

                RechargeDate = r.RechargeDate,

                NetworkOperator = r.NetworkOperator,

                PaymentStatus = r.PaymentStatus,

                RechargeStatus = r.RechargeStatus

            })

            .ToList();



        ViewBag.DeviceStatusMap = data.Devices

            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo))

            .GroupBy(d => d.VehicleNo.Trim(), StringComparer.OrdinalIgnoreCase)

            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().LifecycleStatus) ? g.First().Status : g.First().LifecycleStatus, StringComparer.OrdinalIgnoreCase);



        ViewBag.NetworkOperators = GetBranchNetworkOperators(data);



        var txnMap = new Dictionary<string, string>();

        foreach (var r in recharges)

        {

            var tid = RechargeAuditHelper.FindMatchingTransactionId(data, r);

            if (!string.IsNullOrEmpty(tid))

            {

                txnMap[r.Id] = tid;

            }

        }

        ViewBag.RechargeTxnMap = txnMap;



        var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var localTodayStr = DateTime.UtcNow.AddHours(5.5).ToString("yyyy-MM-dd");

        var pendingTodayCount = recharges.Count(r =>

            string.Equals(r.RechargeStatus, "Pending", StringComparison.OrdinalIgnoreCase) &&

            (string.IsNullOrWhiteSpace(r.RechargeDate) || r.RechargeDate.CompareTo(localTodayStr) <= 0 || r.RechargeDate.CompareTo(todayStr) <= 0));



        // Filter out items that are both Processed and Paid from the main active view (they live in the Processed archive page)
        var activeRecharges = recharges
            .Where(r => !(string.Equals(r.RechargeStatus, "Processed", StringComparison.OrdinalIgnoreCase) 
                       && string.Equals(r.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var vm = new RechargeListViewModel

        {

            Recharges = activeRecharges,

            OutstandingPayments = outstanding,

            TotalProcessed = recharges.Count(r => string.Equals(r.RechargeStatus, "Processed", StringComparison.OrdinalIgnoreCase) && string.Equals(r.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)),

            TotalPending = activeRecharges.Count(r => string.Equals(r.RechargeStatus, "Pending", StringComparison.OrdinalIgnoreCase)),

            TotalPaid = activeRecharges.Count(r => string.Equals(r.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase)),

            TotalNotPaid = activeRecharges.Count(r => string.Equals(r.PaymentStatus, "Not Paid", StringComparison.OrdinalIgnoreCase)),

            OutstandingCount = outstanding.Count,

            PendingTodayCount = pendingTodayCount

        };



        return View(vm);

    }



    [HttpGet]

    public async Task<IActionResult> PendingAlert()

    {

        var recharges = await _store.GetRechargesAsync();

        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var localTodayStr = DateTime.UtcNow.AddHours(5.5).ToString("yyyy-MM-dd");



        var pending = recharges

            .Where(r => (string.Equals(r.Branch, branch, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(r.Branch))

                        && string.Equals(r.RechargeStatus, "Pending", StringComparison.OrdinalIgnoreCase)

                        && (string.IsNullOrWhiteSpace(r.RechargeDate) || r.RechargeDate.CompareTo(localTodayStr) <= 0 || r.RechargeDate.CompareTo(todayStr) <= 0))

            .OrderByDescending(r => r.CreatedAt)

            .Select(r => new

            {

                id = r.Id,

                vehicleNo = r.VehicleNo ?? "",

                imei = r.Imei ?? "",

                iccid = r.Iccid ?? "",

                vendor = r.Vendor ?? "",

                operatorName = r.NetworkOperator ?? "",

                rechargeDate = r.RechargeDate ?? ""

            })

            .ToList();



        return Json(new

        {

            count = pending.Count,

            items = pending

        });

    }



    [HttpPost]

    [RequireModulePermission("Recharges", PermissionLevel.Edit)]

    public async Task<IActionResult> BulkSave([FromBody] List<RechargeFormViewModel> rows)

    {

        if (rows == null || rows.Count == 0) return BadRequest(new { success = false, message = "No recharge rows provided." });



        foreach (var row in rows)

        {

            var missing = new List<string>();

            if (string.IsNullOrWhiteSpace(row.VehicleNo)) missing.Add("Vehicle No");

            if (string.IsNullOrWhiteSpace(row.Imei)) missing.Add("IMEI");

            if (string.IsNullOrWhiteSpace(row.Iccid)) missing.Add("ICCID");

            if (string.IsNullOrWhiteSpace(row.SimAirtel)) missing.Add("SIM - Airtel");

            if (string.IsNullOrWhiteSpace(row.SimBsnl)) missing.Add("SIM - BSNL");

            if (string.IsNullOrWhiteSpace(row.Vendor)) missing.Add("Vendor");

            if (string.IsNullOrWhiteSpace(row.NetworkOperator)) missing.Add("Network Operator");

            if (string.IsNullOrWhiteSpace(row.RechargeDate)) missing.Add("Recharge Date");

            if (string.IsNullOrWhiteSpace(row.PreviousExpiryDate)) missing.Add("Prev. Expiry Date");



            if (missing.Count > 0)

            {

                return BadRequest(new { success = false, message = $"All fields are required. Please fill in: {string.Join(", ", missing)}" });

            }

        }



        // Intra-batch duplicate check

        for (int i = 0; i < rows.Count; i++)

        {

            for (int j = i + 1; j < rows.Count; j++)

            {

                if (RechargeAuditHelper.IsSameDevice(rows[i].Imei, rows[i].Iccid, rows[i].VehicleNo, rows[j].Imei, rows[j].Iccid, rows[j].VehicleNo) &&

                    RechargeAuditHelper.IsSameRechargePeriod(rows[i].ExpiryDate, rows[i].PreviousExpiryDate, rows[j].ExpiryDate, rows[j].PreviousExpiryDate))

                {

                    return BadRequest(new { success = false, duplicate = true, message = "Recharge record is already present in this submission." });

                }

            }

        }



        // Database duplicate check

        var existing = await _store.GetRechargesAsync();

        foreach (var row in rows)

        {

            var dup = RechargeAuditHelper.FindDuplicate(existing, row);

            if (dup != null)

            {

                return BadRequest(new { success = false, duplicate = true, message = "Recharge record is already present." });

            }

        }



        await _store.SaveRechargesAsync(rows);

        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        await _store.UpdateAsync(d => RechargeAuditHelper.AuditProcessedRechargePayments(d, branch));

        return Json(new { success = true });

    }



    [HttpPost]

    [RequireModulePermission("Recharges", PermissionLevel.Edit)]

    public async Task<IActionResult> Delete(string id)

    {

        await _store.DeleteRechargeAsync(id);

        return Json(new { success = true });

    }



    [HttpGet]

    public async Task<IActionResult> Lookup(string? query)

    {

        if (string.IsNullOrWhiteSpace(query))

            return Json(new { });



        var data = await _store.SnapshotAsync();

        query = query.Trim();



        var device = data.Devices.FirstOrDefault(d =>

            string.Equals(d.VehicleNo, query, StringComparison.OrdinalIgnoreCase) ||

            string.Equals(d.Imei, query, StringComparison.OrdinalIgnoreCase) ||

            string.Equals(d.Iccid, query, StringComparison.OrdinalIgnoreCase) ||

            (!string.IsNullOrWhiteSpace(d.SimAirtel) && string.Equals(d.SimAirtel, query, StringComparison.OrdinalIgnoreCase)) ||

            (!string.IsNullOrWhiteSpace(d.SimBsnl) && string.Equals(d.SimBsnl, query, StringComparison.OrdinalIgnoreCase)));



        if (device == null)

            return Json(new { found = false });



        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

        DeviceLifecycleHelper.SyncDevice(device, config);



        var parsedExpiry = DateValue.Parse(device.LatestExpiryDate)?.ToString("yyyy-MM-dd")

            ?? (device.LatestExpiryDate?.Length >= 10 ? device.LatestExpiryDate.Substring(0, 10) : device.LatestExpiryDate);



        var currentStatus = string.IsNullOrWhiteSpace(device.LifecycleStatus) ? device.Status : device.LifecycleStatus;



        return Json(new

        {

            found = true,

            vehicleNo = device.VehicleNo,

            imei = device.Imei,

            iccid = device.Iccid,

            simAirtel = device.SimAirtel,

            simBsnl = device.SimBsnl,

            vendor = device.Vendor,

            networkOperator = device.NetworkOperator,

            activationDate = device.ActivationDate,

            latestExpiryDate = parsedExpiry,

            type = device.Type,

            status = currentStatus

        });

    }



    [HttpGet]

    public async Task<IActionResult> Processed()

    {

        var data = await _store.SnapshotAsync();

        var rawRecharges = await _store.GetRechargesAsync();

        var recharges = RechargeAuditHelper.FilterDuplicates(rawRecharges);

        var processed = recharges

            .Where(r => string.Equals(r.RechargeStatus, "Processed", StringComparison.OrdinalIgnoreCase)
                     && string.Equals(r.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))

            .OrderByDescending(r => r.RechargeDate)

            .ThenByDescending(r => r.CreatedAt)

            .ToList();



        ViewBag.DeviceStatusMap = data.Devices

            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo))

            .GroupBy(d => d.VehicleNo.Trim(), StringComparer.OrdinalIgnoreCase)

            .ToDictionary(g => g.Key, g => string.IsNullOrWhiteSpace(g.First().LifecycleStatus) ? g.First().Status : g.First().LifecycleStatus, StringComparer.OrdinalIgnoreCase);



        ViewBag.TotalProcessed = processed.Count;

        ViewBag.NetworkOperators = GetBranchNetworkOperators(data);



        var procTxnMap = new Dictionary<string, string>();

        foreach (var r in processed)

        {

            var tid = RechargeAuditHelper.FindMatchingTransactionId(data, r);

            if (!string.IsNullOrEmpty(tid))

            {

                procTxnMap[r.Id] = tid;

            }

        }

        ViewBag.RechargeTxnMap = procTxnMap;



        return View(processed);

    }



    [HttpGet]

    public async Task<IActionResult> OpenTransaction(string id, string? vehicleNo)

    {

        var data = await _store.SnapshotAsync();

        var recharges = await _store.GetRechargesAsync();

        var recharge = recharges.FirstOrDefault(r => r.Id == id);

        var txnId = RechargeAuditHelper.FindMatchingTransactionId(data, recharge, vehicleNo);

        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        if (!string.IsNullOrEmpty(txnId))

        {

            return RedirectToAction("Edit", "Transactions", new { area = branch, id = txnId, rechargeId = id });

        }

        return RedirectToAction("Create", "Transactions", new { area = branch, vehicleNo = vehicleNo ?? recharge?.VehicleNo, paymentFor = "None", rechargeId = id });

    }



    [HttpGet]

    public async Task<IActionResult> Export([FromQuery] string? q = null, [FromQuery] string? status = null, [FromQuery] string? payment = null, [FromQuery] string? op = null)

    {

        var data = await _store.SnapshotAsync();

        var recharges = RechargeAuditHelper.FilterDuplicates(data.Recharges).AsEnumerable();



        if (!string.IsNullOrWhiteSpace(status))

            recharges = recharges.Where(r => string.Equals(r.RechargeStatus, status, StringComparison.OrdinalIgnoreCase));



        if (!string.IsNullOrWhiteSpace(payment))

            recharges = recharges.Where(r => string.Equals(r.PaymentStatus, payment, StringComparison.OrdinalIgnoreCase));



        if (!string.IsNullOrWhiteSpace(op))

            recharges = recharges.Where(r => (r.NetworkOperator ?? "").Contains(op.Trim(), StringComparison.OrdinalIgnoreCase));



        if (!string.IsNullOrWhiteSpace(q))

        {

            var term = q.Trim().ToLowerInvariant();

            recharges = recharges.Where(r =>

                (r.VehicleNo ?? "").ToLowerInvariant().Contains(term) ||

                (r.Imei ?? "").ToLowerInvariant().Contains(term) ||

                (r.Iccid ?? "").ToLowerInvariant().Contains(term) ||

                (r.SimAirtel ?? "").ToLowerInvariant().Contains(term) ||

                (r.SimBsnl ?? "").ToLowerInvariant().Contains(term) ||

                (r.Vendor ?? "").ToLowerInvariant().Contains(term) ||

                (r.NetworkOperator ?? "").ToLowerInvariant().Contains(term) ||

                (r.Notes ?? "").ToLowerInvariant().Contains(term));

        }



        var sb = new StringBuilder();

        sb.AppendLine("Vehicle No,IMEI,ICCID,Airtel SIM,BSNL SIM,Vendor,Network Operator,Payment Status,Recharge Status,Activation Date,Previous Expiry Date,New Expiry Date,Recharge Date,Notes");

        foreach (var r in recharges.OrderByDescending(x => x.RechargeDate))

        {

            string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

            sb.AppendLine(string.Join(",", Esc(r.VehicleNo), Esc(r.Imei), Esc(r.Iccid), Esc(r.SimAirtel), Esc(r.SimBsnl), Esc(r.Vendor), Esc(r.NetworkOperator), Esc(r.PaymentStatus), Esc(r.RechargeStatus), Esc(r.ActivationDate), Esc(r.PreviousExpiryDate), Esc(r.ExpiryDate), Esc(r.RechargeDate), Esc(r.Notes)));

        }

        var branch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"recharge-list-{branch}-{DateTime.Now:yyyyMMdd}.csv");

    }



    private static List<string> GetBranchNetworkOperators(AppData data)

    {

        return new List<string> { "Taisys", "Sensorise" };

    }

}