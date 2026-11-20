using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Data;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;
using System.Text;

namespace MygtsMvc.Controllers;

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

        var recharges = await _store.GetRechargesAsync();

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
            OutstandingCount = outstanding.Count
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PendingAlert()
    {
        var recharges = await _store.GetRechargesAsync();
        var pending = recharges
            .Where(r => string.Equals(r.RechargeStatus, "Pending", StringComparison.OrdinalIgnoreCase))
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
        if (rows == null) return BadRequest();
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

        var parsedExpiry = DateValue.Parse(device.LatestExpiryDate)?.ToString("yyyy-MM-dd")
            ?? (device.LatestExpiryDate?.Length >= 10 ? device.LatestExpiryDate.Substring(0, 10) : device.LatestExpiryDate);

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
            status = string.IsNullOrWhiteSpace(device.LifecycleStatus) ? device.Status : device.LifecycleStatus
        });
    }

    [HttpGet]
    public async Task<IActionResult> Processed()
    {
        var data = await _store.SnapshotAsync();
        var recharges = await _store.GetRechargesAsync();
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
        return View(processed);
    }

    [HttpGet]
    public async Task<IActionResult> Export([FromQuery] string? q = null, [FromQuery] string? status = null, [FromQuery] string? payment = null)
    {
        var data = await _store.SnapshotAsync();
        var recharges = data.Recharges.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(status))
            recharges = recharges.Where(r => string.Equals(r.RechargeStatus, status, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(payment))
            recharges = recharges.Where(r => string.Equals(r.PaymentStatus, payment, StringComparison.OrdinalIgnoreCase));

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
}