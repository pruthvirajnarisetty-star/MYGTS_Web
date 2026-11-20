using System;
using System.Collections.Generic;
using System.Linq;
using MygtsMvc.Models;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Services;

public static class RechargeAuditHelper
{
    public static int AuditProcessedRechargePayments(AppData data, string currentBranch)
    {
        if (data.Recharges == null || data.Recharges.Count == 0) return 0;

        var changesCount = 0;

        var devByVehicle = data.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo))
            .GroupBy(d => d.VehicleNo.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var devByImei = data.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Imei))
            .GroupBy(d => d.Imei.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var devByIccid = data.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Iccid))
            .GroupBy(d => d.Iccid.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var processedUnpaid = data.Recharges
            .Where(r => string.Equals(r.RechargeStatus, "Processed", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(r.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var r in processedUnpaid)
        {
            var vNo = r.VehicleNo?.Trim();
            var imei = r.Imei?.Trim();
            var iccid = r.Iccid?.Trim();

            DeviceRecord? dev = null;
            if (!string.IsNullOrEmpty(vNo) && devByVehicle.TryGetValue(vNo, out var d1)) dev = d1;
            else if (!string.IsNullOrEmpty(imei) && devByImei.TryGetValue(imei, out var d2)) dev = d2;
            else if (!string.IsNullOrEmpty(iccid) && devByIccid.TryGetValue(iccid, out var d3)) dev = d3;

            if (dev != null)
            {
                if (string.IsNullOrEmpty(vNo)) vNo = dev.VehicleNo?.Trim();
                if (string.IsNullOrEmpty(imei)) imei = dev.Imei?.Trim();
                if (string.IsNullOrEmpty(iccid)) iccid = dev.Iccid?.Trim();
            }

            var rechargeDate = DateValue.Parse(r.RechargeDate) ?? (DateValue.Parse(r.CreatedAt) ?? DateTime.MinValue.Date);

            // Verify with transaction page against ICCID / IMEI / VehicleNo on or after recharge date
            var matchingPaidTxn = data.Transactions.FirstOrDefault(t =>
            {
                bool vMatch = !string.IsNullOrEmpty(vNo) && string.Equals(t.VehicleNo?.Trim(), vNo, StringComparison.OrdinalIgnoreCase);

                bool imeiMatch = !string.IsNullOrEmpty(imei) && (
                    (!string.IsNullOrEmpty(t.Extras) && t.Extras.Contains(imei, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(t.Notes) && t.Notes.Contains(imei, StringComparison.OrdinalIgnoreCase)));

                bool iccidMatch = !string.IsNullOrEmpty(iccid) && (
                    (!string.IsNullOrEmpty(t.Extras) && t.Extras.Contains(iccid, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(t.Notes) && t.Notes.Contains(iccid, StringComparison.OrdinalIgnoreCase)));

                if (!vMatch && !imeiMatch && !iccidMatch) return false;

                var txnDate = DateValue.Parse(t.Date);
                if (txnDate.HasValue && txnDate.Value < rechargeDate) return false;

                return (t.Amount ?? 0m) > 0m;
            });

            if (matchingPaidTxn != null)
            {
                r.PaymentStatus = "Paid";
                changesCount++;
            }
            else
            {
                var reminderKey = $"Recharge:{r.Id}";
                var displayId = !string.IsNullOrEmpty(vNo) ? vNo : (!string.IsNullOrEmpty(imei) ? $"IMEI {imei}" : $"Recharge #{r.Id.Substring(0, Math.Min(6, r.Id.Length))}");
                var recDateStr = string.IsNullOrWhiteSpace(r.RechargeDate) ? r.CreatedAt : r.RechargeDate;

                bool alreadyAdded = data.ManualReminders.Any(m => 
                    !m.IsComplete && (
                        (m.Details != null && m.Details.Contains(reminderKey)) ||
                        (m.Title != null && m.Title.Contains(displayId, StringComparison.OrdinalIgnoreCase) && m.Title.Contains("Recharge Payment", StringComparison.OrdinalIgnoreCase))
                    ));

                if (!alreadyAdded)
                {
                    data.ManualReminders.Add(new ManualReminder
                    {
                        Title = $"Recharge Payment Pending: {displayId}",
                        Details = $"[{reminderKey}] Recharge was processed on {recDateStr} (Vendor: {r.Vendor ?? "—"}, Operator: {r.NetworkOperator ?? "—"}, IMEI: {imei ?? "—"}, ICCID: {iccid ?? "—"}), but no payment transaction was recorded after recharge date. Follow up with client/vendor for payment collection.",
                        DueDate = recDateStr.Length >= 10 ? recDateStr.Substring(0, 10) : DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        Branch = !string.IsNullOrEmpty(r.Branch) ? r.Branch : currentBranch,
                        CreatedBy = "System (Recharge Audit)",
                        TargetAudience = "@all",
                        IsComplete = false
                    });
                    changesCount++;
                }
            }
        }

        return changesCount;
    }

    public static string? FindMatchingTransactionId(AppData data, RechargeRecord? r, string? fallbackVehicleNo = null)
    {
        var txn = FindMatchingTransaction(data, r, fallbackVehicleNo);
        return txn?.Id;
    }

    public static TransactionRecord? FindMatchingTransaction(AppData data, RechargeRecord? r, string? fallbackVehicleNo = null)
    {
        if (data.Transactions == null || data.Transactions.Count == 0) return null;

        var vNo = r?.VehicleNo?.Trim() ?? fallbackVehicleNo?.Trim();
        var imei = r?.Imei?.Trim();
        var iccid = r?.Iccid?.Trim();

        if (string.IsNullOrEmpty(vNo) && string.IsNullOrEmpty(imei) && string.IsNullOrEmpty(iccid))
            return null;

        if (string.IsNullOrEmpty(imei) || string.IsNullOrEmpty(iccid) || string.IsNullOrEmpty(vNo))
        {
            var dev = data.Devices.FirstOrDefault(d =>
                (!string.IsNullOrEmpty(vNo) && string.Equals(d.VehicleNo?.Trim(), vNo, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(imei) && string.Equals(d.Imei?.Trim(), imei, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(iccid) && string.Equals(d.Iccid?.Trim(), iccid, StringComparison.OrdinalIgnoreCase)));
            if (dev != null)
            {
                if (string.IsNullOrEmpty(vNo)) vNo = dev.VehicleNo?.Trim();
                if (string.IsNullOrEmpty(imei)) imei = dev.Imei?.Trim();
                if (string.IsNullOrEmpty(iccid)) iccid = dev.Iccid?.Trim();
            }
        }

        var rechargeDate = r != null ? (DateValue.Parse(r.RechargeDate) ?? (DateValue.Parse(r.CreatedAt) ?? DateTime.MinValue.Date)) : DateTime.MinValue.Date;

        var candidateTxns = data.Transactions.Where(t =>
        {
            bool vMatch = !string.IsNullOrEmpty(vNo) && string.Equals(t.VehicleNo?.Trim(), vNo, StringComparison.OrdinalIgnoreCase);

            bool imeiMatch = !string.IsNullOrEmpty(imei) && (
                string.Equals(t.Imei?.Trim(), imei, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(t.Extras) && t.Extras.Contains(imei, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.Notes) && t.Notes.Contains(imei, StringComparison.OrdinalIgnoreCase)));

            bool iccidMatch = !string.IsNullOrEmpty(iccid) && (
                (!string.IsNullOrEmpty(t.Extras) && t.Extras.Contains(iccid, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(t.Notes) && t.Notes.Contains(iccid, StringComparison.OrdinalIgnoreCase)));

            return (vMatch || imeiMatch || iccidMatch) && (t.Amount ?? 0m) > 0m;
        }).ToList();

        if (candidateTxns.Count == 0) return null;

        // 1. First priority: transaction on or after recharge date, prioritizing "Recharge"/"Renewal"/"Reactivation"
        var onOrAfter = candidateTxns
            .Where(t =>
            {
                var txnDate = DateValue.Parse(t.Date);
                return !txnDate.HasValue || txnDate.Value >= rechargeDate;
            })
            .OrderByDescending(t => (t.PaymentFor ?? "").Contains("Recharge", StringComparison.OrdinalIgnoreCase) ||
                                    (t.PaymentFor ?? "").Contains("Renewal", StringComparison.OrdinalIgnoreCase) ||
                                    (t.PaymentFor ?? "").Contains("Reactivation", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => DateValue.Parse(t.Date) ?? DateTime.MinValue)
            .ThenByDescending(t => t.CreatedAt)
            .FirstOrDefault();

        if (onOrAfter != null) return onOrAfter;

        // 2. Second priority: latest matching candidate transaction
        return candidateTxns
            .OrderByDescending(t => (t.PaymentFor ?? "").Contains("Recharge", StringComparison.OrdinalIgnoreCase) ||
                                    (t.PaymentFor ?? "").Contains("Renewal", StringComparison.OrdinalIgnoreCase) ||
                                    (t.PaymentFor ?? "").Contains("Reactivation", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => DateValue.Parse(t.Date) ?? DateTime.MinValue)
            .ThenByDescending(t => t.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks if two records represent the same device by cross-checking Vehicle No, IMEI and ICCID.
    /// Handles matching Vehicle-to-Vehicle, IMEI-to-IMEI, ICCID-to-ICCID, or cross-matching.
    /// </summary>
    public static bool IsSameDevice(string? imeiA, string? iccidA, string? vehicleA,
                                    string? imeiB, string? iccidB, string? vehicleB)
    {
        var cleanVehA = vehicleA?.Trim();
        var cleanVehB = vehicleB?.Trim();
        var cleanImeiA = imeiA?.Trim();
        var cleanImeiB = imeiB?.Trim();
        var cleanIccidA = iccidA?.Trim();
        var cleanIccidB = iccidB?.Trim();

        bool vehMatch = !string.IsNullOrEmpty(cleanVehA) && !string.IsNullOrEmpty(cleanVehB) &&
                        string.Equals(cleanVehA, cleanVehB, StringComparison.OrdinalIgnoreCase);

        bool imeiMatch = !string.IsNullOrEmpty(cleanImeiA) && !string.IsNullOrEmpty(cleanImeiB) &&
                         string.Equals(cleanImeiA, cleanImeiB, StringComparison.OrdinalIgnoreCase);

        bool iccidMatch = !string.IsNullOrEmpty(cleanIccidA) && !string.IsNullOrEmpty(cleanIccidB) &&
                          string.Equals(cleanIccidA, cleanIccidB, StringComparison.OrdinalIgnoreCase);

        bool crossMatch1 = !string.IsNullOrEmpty(cleanImeiA) && !string.IsNullOrEmpty(cleanIccidB) &&
                           string.Equals(cleanImeiA, cleanIccidB, StringComparison.OrdinalIgnoreCase);

        bool crossMatch2 = !string.IsNullOrEmpty(cleanIccidA) && !string.IsNullOrEmpty(cleanImeiB) &&
                           string.Equals(cleanIccidA, cleanImeiB, StringComparison.OrdinalIgnoreCase);

        return vehMatch || imeiMatch || iccidMatch || crossMatch1 || crossMatch2;
    }

    public static bool IsSameDevice(string? imeiA, string? iccidA, string? imeiB, string? iccidB)
    {
        return IsSameDevice(imeiA, iccidA, null, imeiB, iccidB, null);
    }

    /// <summary>
    /// Checks if two expiry date strings represent the same date.
    /// Annual renewals for next year return false so they are not treated as duplicates.
    /// </summary>
    public static bool IsSameExpiryDate(string? expA, string? expB)
    {
        if (string.IsNullOrWhiteSpace(expA) || string.IsNullOrWhiteSpace(expB))
            return false;

        var dateA = DateValue.Parse(expA);
        var dateB = DateValue.Parse(expB);

        if (dateA.HasValue && dateB.HasValue)
        {
            return dateA.Value.Date == dateB.Value.Date;
        }

        return string.Equals(expA.Trim(), expB.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares two recharge periods. If both have a new expiry date, compares those.
    /// If new expiry date is omitted, compares previous expiry date.
    /// Returns true if they represent the same renewal cycle (duplicate).
    /// </summary>
    public static bool IsSameRechargePeriod(string? newExpA, string? prevExpA,
                                           string? newExpB, string? prevExpB)
    {
        var hasNewA = !string.IsNullOrWhiteSpace(newExpA);
        var hasNewB = !string.IsNullOrWhiteSpace(newExpB);

        if (hasNewA && hasNewB)
        {
            return IsSameExpiryDate(newExpA, newExpB);
        }

        if (!hasNewA && !hasNewB)
        {
            var hasPrevA = !string.IsNullOrWhiteSpace(prevExpA);
            var hasPrevB = !string.IsNullOrWhiteSpace(prevExpB);
            if (hasPrevA && hasPrevB)
            {
                return IsSameExpiryDate(prevExpA, prevExpB);
            }
            return true;
        }

        // One has New Expiry, one does not.
        // Compare the specified new expiry against the other's previous expiry.
        var specifiedNew = hasNewA ? newExpA : newExpB;
        var otherPrev = hasNewA ? prevExpB : prevExpA;

        if (!string.IsNullOrWhiteSpace(specifiedNew) && !string.IsNullOrWhiteSpace(otherPrev))
        {
            return IsSameExpiryDate(specifiedNew, otherPrev);
        }

        return false;
    }

    /// <summary>
    /// Checks whether an incoming recharge row is a duplicate of an existing record.
    /// Cross-checks Vehicle No/IMEI/ICCID and renewal period, excluding candidate's own Id when updating.
    /// </summary>
    public static RechargeRecord? FindDuplicate(IEnumerable<RechargeRecord> existingRecharges, RechargeFormViewModel candidate)
    {
        var candVeh = candidate.VehicleNo?.Trim();
        var candImei = candidate.Imei?.Trim();
        var candIccid = candidate.Iccid?.Trim();
        var candNewExp = candidate.ExpiryDate?.Trim();
        var candPrevExp = candidate.PreviousExpiryDate?.Trim();

        if (string.IsNullOrEmpty(candVeh) && string.IsNullOrEmpty(candImei) && string.IsNullOrEmpty(candIccid))
            return null;

        return existingRecharges.FirstOrDefault(r =>
            (string.IsNullOrWhiteSpace(candidate.Id) || r.Id != candidate.Id) &&
            IsSameDevice(r.Imei, r.Iccid, r.VehicleNo, candImei, candIccid, candVeh) &&
            IsSameRechargePeriod(r.ExpiryDate, r.PreviousExpiryDate, candNewExp, candPrevExp));
    }

    /// <summary>
    /// Deduplicates a collection of recharge records by cross-checking Vehicle No/IMEI/ICCID and period,
    /// keeping the newest record (by CreatedAt / Id) and discarding duplicates.
    /// </summary>
    public static List<RechargeRecord> FilterDuplicates(IEnumerable<RechargeRecord> records)
    {
        if (records == null) return new List<RechargeRecord>();

        var result = new List<RechargeRecord>();

        foreach (var r in records.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id))
        {
            bool isDup = result.Any(existing =>
                IsSameDevice(existing.Imei, existing.Iccid, existing.VehicleNo, r.Imei, r.Iccid, r.VehicleNo) &&
                IsSameRechargePeriod(existing.ExpiryDate, existing.PreviousExpiryDate, r.ExpiryDate, r.PreviousExpiryDate));

            if (!isDup)
            {
                result.Add(r);
            }
        }

        return result;
    }

    /// <summary>
    /// Synchronizes processed recharges into matching devices and transactions.
    /// <summary>
    /// Strictly ensures that dates (ActivationDate and LatestExpiryDate/NewExpiryDate/ExpiryDate)
    /// stay in 100% lockstep across Devices, Transactions, and Recharges for both branches.
    /// Also ensures in DMG branch that 2-3 year older activation dates are updated to the active cycle (1 year prior to expiry).
    /// </summary>
    public static int SyncProcessedRecharges(AppData data)
    {
        if (data == null || data.Devices == null) return 0;

        int updatedCount = 0;

        // 1. Build lookup dictionaries for devices
        var devByVeh = new Dictionary<string, DeviceRecord>(StringComparer.OrdinalIgnoreCase);
        var devByImei = new Dictionary<string, DeviceRecord>(StringComparer.OrdinalIgnoreCase);
        var devByIccid = new Dictionary<string, DeviceRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in data.Devices)
        {
            var v = (d.VehicleNo ?? "").Replace(" ", "").Trim();
            var im = (d.Imei ?? "").Trim();
            var ic = (d.Iccid ?? "").Trim();
            if (!string.IsNullOrEmpty(v) && !devByVeh.ContainsKey(v)) devByVeh[v] = d;
            if (!string.IsNullOrEmpty(im) && !devByImei.ContainsKey(im)) devByImei[im] = d;
            if (!string.IsNullOrEmpty(ic) && !devByIccid.ContainsKey(ic)) devByIccid[ic] = d;
        }

        DeviceRecord? FindDev(string? vNo, string? imei, string? iccid)
        {
            var v = (vNo ?? "").Replace(" ", "").Trim();
            var im = (imei ?? "").Trim();
            var ic = (iccid ?? "").Trim();
            if (!string.IsNullOrEmpty(im) && devByImei.TryGetValue(im, out var d1)) return d1;
            if (!string.IsNullOrEmpty(ic) && devByIccid.TryGetValue(ic, out var d2)) return d2;
            if (!string.IsNullOrEmpty(v) && devByVeh.TryGetValue(v, out var d3)) return d3;
            return null;
        }

        // 2. Fix 2-3 year older activation dates on DMG devices where expiry date was updated
        foreach (var dev in data.Devices.Where(d => string.Equals(d.Branch, "DMG", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(dev.LatestExpiryDate)) continue;
            var expParsed = DateValue.Parse(dev.LatestExpiryDate);
            if (!expParsed.HasValue) continue;

            var actParsed = DateValue.Parse(dev.ActivationDate);
            if (!actParsed.HasValue || (expParsed.Value.Date - actParsed.Value.Date).TotalDays > 400)
            {
                var newAct = expParsed.Value.AddYears(-1).ToString("yyyy-MM-dd");
                dev.ActivationDate = newAct;
                updatedCount++;
            }
        }

        // 3. Processed Recharges sync to Devices and Transactions
        var processed = data.Recharges?
            .Where(r => (string.Equals(r.RechargeStatus, "Processed", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(r.ExpiryDate)) &&
                        !string.IsNullOrWhiteSpace(r.ExpiryDate))
            .ToList() ?? new List<RechargeRecord>();

        foreach (var r in processed)
        {
            var dev = FindDev(r.VehicleNo, r.Imei, r.Iccid);
            var rExp = r.ExpiryDate.Trim();
            var rAct = (r.ActivationDate ?? "").Trim();
            var rVen = (r.Vendor ?? "").Trim();
            var rOp = (r.NetworkOperator ?? "").Trim();

            if (dev != null)
            {
                // Align activation date if empty or in DMG > 400 days
                if (string.IsNullOrWhiteSpace(rAct))
                {
                    rAct = dev.ActivationDate;
                }
                if (string.Equals(dev.Branch, "DMG", StringComparison.OrdinalIgnoreCase) && DateValue.Parse(rExp) is DateTime rExpDt)
                {
                    var rActDt = DateValue.Parse(rAct);
                    if (!rActDt.HasValue || (rExpDt.Date - rActDt.Value.Date).TotalDays > 400)
                    {
                        rAct = rExpDt.AddYears(-1).ToString("yyyy-MM-dd");
                        r.ActivationDate = rAct;
                    }
                }

                dev.LatestExpiryDate = rExp;
                if (!string.IsNullOrWhiteSpace(rAct)) dev.ActivationDate = rAct;
                if (!string.IsNullOrWhiteSpace(rVen)) dev.Vendor = rVen;
                if (!string.IsNullOrWhiteSpace(rOp) && string.IsNullOrWhiteSpace(dev.NetworkOperator)) dev.NetworkOperator = rOp;

                if (DateValue.Parse(dev.LatestExpiryDate) is DateTime expDt && expDt.Date >= DateTime.UtcNow.Date)
                {
                    dev.Status = "Active";
                    dev.LifecycleStatus = "Active";
                    dev.DaysPastExpiry = 0;
                }
                updatedCount++;
            }

            // Match transactions
            if (data.Transactions != null && data.Transactions.Count > 0)
            {
                var rVehClean = (r.VehicleNo ?? "").Replace(" ", "").Trim();
                var rImeiClean = (r.Imei ?? "").Trim();

                var matchingTxns = data.Transactions.Where(t =>
                {
                    if (!string.IsNullOrWhiteSpace(rVehClean) && !string.IsNullOrWhiteSpace(t.VehicleNo) &&
                        string.Equals(t.VehicleNo.Replace(" ", "").Trim(), rVehClean, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (!string.IsNullOrWhiteSpace(rImeiClean) && !string.IsNullOrWhiteSpace(t.Imei) &&
                        string.Equals(t.Imei.Trim(), rImeiClean, StringComparison.OrdinalIgnoreCase))
                        return true;
                    return false;
                }).ToList();

                var rDateObj = DateValue.Parse(r.RechargeDate) ?? DateValue.Parse(r.CreatedAt);
                var targetTxn = matchingTxns.FirstOrDefault(t =>
                    (DateValue.Parse(t.Date) is DateTime tDt && rDateObj.HasValue && tDt >= rDateObj.Value) &&
                    ((t.PaymentFor ?? "").Contains("Recharge", StringComparison.OrdinalIgnoreCase) ||
                     (t.PaymentFor ?? "").Contains("Renewal", StringComparison.OrdinalIgnoreCase) ||
                     (t.PaymentFor ?? "").Contains("Reactivation", StringComparison.OrdinalIgnoreCase)))
                    ?? matchingTxns.FirstOrDefault(t =>
                        (t.PaymentFor ?? "").Contains("Recharge", StringComparison.OrdinalIgnoreCase) ||
                        (t.PaymentFor ?? "").Contains("Renewal", StringComparison.OrdinalIgnoreCase) ||
                        (t.PaymentFor ?? "").Contains("Reactivation", StringComparison.OrdinalIgnoreCase))
                    ?? matchingTxns.OrderByDescending(t => DateValue.Parse(t.Date) ?? DateTime.MinValue).FirstOrDefault();

                if (targetTxn != null)
                {
                    targetTxn.NewExpiryDate = rExp;
                    if (!string.IsNullOrWhiteSpace(rAct)) targetTxn.ActivationDate = rAct;
                    if (!string.IsNullOrWhiteSpace(rVen)) targetTxn.Vendor = rVen;
                }
            }
        }

        // 4. Ensure Transactions match Devices
        if (data.Transactions != null && data.Transactions.Count > 0)
        {
            foreach (var txn in data.Transactions)
            {
                var dev = FindDev(txn.VehicleNo, txn.Imei, null);
                if (dev == null) continue;

                // If transaction date is present and device has matching or older date
                if (!string.IsNullOrWhiteSpace(txn.NewExpiryDate))
                {
                    var tExp = DateValue.Parse(txn.NewExpiryDate);
                    var dExp = DateValue.Parse(dev.LatestExpiryDate);

                    // Fix any erroneous 2029 in DMG where device has correct earlier expiry
                    if (string.Equals(dev.Branch, "DMG", StringComparison.OrdinalIgnoreCase) && tExp.HasValue && tExp.Value.Year >= 2029 && dExp.HasValue && dExp.Value.Year <= 2027)
                    {
                        txn.NewExpiryDate = dev.LatestExpiryDate;
                        txn.ActivationDate = dev.ActivationDate;
                    }
                    else if (tExp.HasValue && (!dExp.HasValue || tExp.Value >= dExp.Value))
                    {
                        dev.LatestExpiryDate = txn.NewExpiryDate;
                        if (!string.IsNullOrWhiteSpace(txn.ActivationDate))
                            dev.ActivationDate = txn.ActivationDate;
                    }
                    else if (dExp.HasValue)
                    {
                        txn.NewExpiryDate = dev.LatestExpiryDate;
                        if (!string.IsNullOrWhiteSpace(dev.ActivationDate))
                            txn.ActivationDate = dev.ActivationDate;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(dev.LatestExpiryDate))
                {
                    txn.NewExpiryDate = dev.LatestExpiryDate;
                    if (!string.IsNullOrWhiteSpace(dev.ActivationDate))
                        txn.ActivationDate = dev.ActivationDate;
                }

                // In DMG, ensure transaction activation date is 1 year prior to expiry
                if (string.Equals(dev.Branch, "DMG", StringComparison.OrdinalIgnoreCase) && DateValue.Parse(txn.NewExpiryDate) is DateTime txnExpDt)
                {
                    var txnActDt = DateValue.Parse(txn.ActivationDate);
                    if (!txnActDt.HasValue || (txnExpDt.Date - txnActDt.Value.Date).TotalDays > 400)
                    {
                        txn.ActivationDate = txnExpDt.AddYears(-1).ToString("yyyy-MM-dd");
                    }
                }
            }
        }

        return updatedCount;
    }
}

