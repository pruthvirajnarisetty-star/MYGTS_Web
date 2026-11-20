using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;

namespace MygtsMvc.Areas.RTO.Controllers;
using Microsoft.AspNetCore.Mvc;

[Area("RTO")]
[Authorize]
public sealed class AlertsController : Controller
{
    private readonly IAppDataStore _store;

    public AlertsController(IAppDataStore store) => _store = store;

    [HttpGet]
    public async Task<IActionResult> Feed()
    {
        var data = await _store.SnapshotAsync();
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var isExecutive = User.IsExecutive();
        var canViewReminders = User.CanViewModule("Reminders");
        var canEditReminders = User.CanEditModule("Reminders");
        var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);

        // Synchronize all devices with current date and grace period settings
        foreach (var d in data.Devices)
        {
            DeviceLifecycleHelper.SyncDevice(d, config);
        }

        var allFleetDevices = data.Devices
            .Where(d => !string.IsNullOrWhiteSpace(d.VehicleNo)
                        && !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase)
                        && !d.IsNotInUse)
            .ToList();

        // 1. Overdue & Expired Alerts (Suspension window: Days 1 to 30 past expiry)
        // Pinned at the top of the alert drawer with urgent red background until renewed or snoozed today
        var overdueAlerts = isExecutive
            ? allFleetDevices
                .Where(d => {
                    var daysLeft = DateValue.DaysFromNow(d.LatestExpiryDate);
                    var daysPast = d.DaysPastExpiry ?? (daysLeft.HasValue ? -daysLeft.Value : 0);
                    return daysLeft.HasValue 
                        && daysLeft.Value < 0 
                        && daysPast <= 30
                        && (string.IsNullOrWhiteSpace(d.InformedDate) || d.InformedDate != todayStr);
                })
                .OrderByDescending(d => d.DaysPastExpiry ?? 0)
                .ToList()
            : new List<DeviceRecord>();

        // 2. Devices expiring this week (0 to 7 days remaining), visible to Executive and above
        var expiringWeekAlerts = isExecutive
            ? allFleetDevices
                .Where(d => DateValue.DaysFromNow(d.LatestExpiryDate) is >= 0 and <= 7
                            && (string.IsNullOrWhiteSpace(d.InformedDate) || d.InformedDate != todayStr))
                .OrderBy(d => DateValue.DaysFromNow(d.LatestExpiryDate))
                .ToList()
            : new List<DeviceRecord>();

        // 3. Recent Lifecycle Shift Alerts
        int suspensionDays = 30;
        int terminationDays = 20;
        if (config.TryGetValue("SUSPENSION_DAYS", out var sStr) && int.TryParse(sStr, out var sVal)) suspensionDays = sVal;
        if (config.TryGetValue("TERMINATION_DAYS", out var tStr) && int.TryParse(tStr, out var tVal)) terminationDays = tVal;

        var lifecycleShifts = isExecutive
            ? allFleetDevices
                .Select(d => {
                    var daysPast = d.DaysPastExpiry ?? (DateValue.DaysFromNow(d.LatestExpiryDate).HasValue ? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value : 0);
                    string? fromStatus = null;
                    string? toStatus = null;
                    string? shiftType = null;
                    string? desc = null;
                    string? filterUrl = null;

                    bool isSensorise = string.Equals(d.NetworkOperator, "Sensorise", StringComparison.OrdinalIgnoreCase);
                    int dSuspension = (isSensorise && config.TryGetValue("SENSORISE_SUSPENSION_DAYS", out var ss) && int.TryParse(ss, out var ssVal)) ? ssVal : suspensionDays;
                    int dTermination = (isSensorise && config.TryGetValue("SENSORISE_TERMINATION_DAYS", out var st) && int.TryParse(st, out var stVal)) ? stVal : terminationDays;

                    if (string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase) && daysPast is >= 1 and <= 3)
                    {
                        fromStatus = "Active";
                        toStatus = "Suspension";
                        shiftType = "Suspension";
                        desc = "Entered Suspension window (grace period active)";
                        filterUrl = "/Devices?Lifecycle=Suspension";
                    }
                    else if (string.Equals(d.LifecycleStatus, "Terminated", StringComparison.OrdinalIgnoreCase) && daysPast > dSuspension && daysPast <= dSuspension + 7)
                    {
                        fromStatus = "Suspension";
                        toStatus = "Terminated";
                        shiftType = "Terminated";
                        desc = "Suspension window ended - Status shifted to Terminated";
                        filterUrl = "/Devices?Lifecycle=Terminated";
                    }
                    else if (string.Equals(d.LifecycleStatus, "Dead", StringComparison.OrdinalIgnoreCase) && daysPast > (dSuspension + dTermination) && daysPast <= (dSuspension + dTermination + 7))
                    {
                        fromStatus = "Terminated";
                        toStatus = "Dead";
                        shiftType = "Dead";
                        desc = "Termination window ended - Status shifted to Dead / Deactivated";
                        filterUrl = "/Devices?Lifecycle=Dead";
                    }

                    if (fromStatus == null) return null;

                    return new LifecycleShiftAlert
                    {
                        VehicleNo = d.VehicleNo,
                        Owner = d.Owner ?? "",
                        CellNo = d.CellNo ?? "",
                        Vendor = d.Vendor ?? "",
                        FromStatus = fromStatus,
                        ToStatus = toStatus ?? "",
                        ShiftType = shiftType ?? "",
                        Description = desc ?? "",
                        FilterUrl = filterUrl ?? "",
                        ExpiryStr = DateValue.Short(d.LatestExpiryDate),
                        DaysPast = daysPast
                    };
                })
                .Where(s => s != null)
                .Select(s => s!)
                .OrderByDescending(s => s.DaysPast)
                .ToList()
            : new List<LifecycleShiftAlert>();

        // 4. Follow-ups visible to anyone addressed (even if Reminders module is restricted for them)
        var openFollowups = data.ManualReminders
            .Where(r => !r.IsComplete 
                        && (string.Equals(r.Branch, currentBranch, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(r.Branch))
                        && User.IsAddressedInReminder(r))
            .OrderBy(r => DateValue.Parse(r.DueDate))
            .ToList();

        var vendorPhoneMap = data.Vendors
            .Where(v => !string.IsNullOrWhiteSpace(v.Name) && !string.IsNullOrWhiteSpace(v.Phone))
            .GroupBy(v => v.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Phone.Trim(), StringComparer.OrdinalIgnoreCase);

        var overdueDeviceItems = overdueAlerts.Select(alertDev =>
        {
            var daysPast = alertDev.DaysPastExpiry ?? Math.Abs(DateValue.DaysFromNow(alertDev.LatestExpiryDate) ?? 0);
            var urgencyClass = "urgent-overdue";
            var urgencyLabel = $"EXPIRED ({daysPast}d ago)";

            var rawPhone = alertDev.CellNo?.Trim() ?? "";
            var cleanPhone = new string(rawPhone.Where(char.IsDigit).ToArray());
            if (cleanPhone.Length == 10) cleanPhone = "91" + cleanPhone;

            var vendorName = alertDev.Vendor?.Trim() ?? "";
            vendorPhoneMap.TryGetValue(vendorName, out var vendorPhone);
            var cleanVendorPhone = new string((vendorPhone ?? "").Where(char.IsDigit).ToArray());
            if (cleanVendorPhone.Length == 10) cleanVendorPhone = "91" + cleanVendorPhone;

            var expiryStr = DateValue.Short(alertDev.LatestExpiryDate);
            var vendorDisp = string.IsNullOrWhiteSpace(vendorName) ? "Vendor" : vendorName;
            
            var msgOwner = $"*MYGTS SERVICES*\n\u200B\nDear Customer,\n\u200B\nYour *One State One GPS* recharge for vehicle *{alertDev.VehicleNo}* expired on *{expiryStr}* and is currently under suspension.\n\u200B\nKindly renew the recharge immediately to avoid termination and interruption of GPS tracking services.\n\u200B\nThank you,\n*MYGTS SERVICES*\n\u200B\n--------------------------------\n\u200B\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*\n\u200B\nಪ್ರಿಯ ಗ್ರಾಹಕರೇ,\n\u200B\nನಿಮ್ಮ ವಾಹನ *{alertDev.VehicleNo}* ನ ಒಂದು ರಾಜ್ಯ ಒಂದು ಜಿಪಿಎಸ್ (One State One GPS) ರಿಚಾರ್ಜ್ *{expiryStr}* ರಂದು ಮುಕ್ತಾಯಗೊಂಡಿದೆ ಮತ್ತು ಪ್ರಸ್ತುತ ಅಮಾನತುಗೊಂಡಿದೆ.\n\u200B\nಜಿಪಿಎಸ್ ಟ್ರ್ಯಾಕಿಂಗ್ ಸೇವೆಗಳಲ್ಲಿನ ಅಡಚಣೆಯನ್ನು ತಪ್ಪಿಸಲು ದಯವಿಟ್ಟು ತಕ್ಷಣವೇ ರಿಚಾರ್ಜ್ ಅನ್ನು ನವೀಕರಿಸಿ (Renew).\n\u200B\nಧನ್ಯವಾದಗಳು,\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*";
            
            var msgVendor = $"*MYGTS SERVICES*\n\u200B\nDear {vendorDisp},\n\u200B\nThe GPS recharge for your client's vehicle *{alertDev.VehicleNo}* expired on *{expiryStr}* and is now under suspension.\n\u200B\nPlease ensure the renewal is completed immediately to avoid permanent service termination.\n\u200B\nThank you,\n*MYGTS SERVICES*\n\u200B\n--------------------------------\n\u200B\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*\n\u200B\nಪ್ರಿಯ {vendorDisp},\n\u200B\nನಿಮ್ಮ ಕ್ಲೈಂಟ್‌ನ ವಾಹನ *{alertDev.VehicleNo}* ನ ಒಂದು ರಾಜ್ಯ ಒಂದು ಜಿಪಿಎಸ್ (One State One GPS) ರಿಚಾರ್ಜ್ *{expiryStr}* ರಂದು ಮುಕ್ತಾಯಗೊಂಡಿದೆ.\n\u200B\nಸೇವೆಯ ಶಾಶ್ವತ ರದ್ದತಿಯನ್ನು ತಪ್ಪಿಸಲು ದಯವಿಟ್ಟು ತಕ್ಷಣವೇ ರಿಚಾರ್ಜ್ ಅನ್ನು ನವೀಕರಿಸಿ.\n\u200B\nಧನ್ಯವಾದಗಳು,\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*";

            var waOwnerUrl = string.IsNullOrWhiteSpace(cleanPhone)
                ? $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(msgOwner)}"
                : $"https://web.whatsapp.com/send?phone={cleanPhone}&text={Uri.EscapeDataString(msgOwner)}";
            var waVendorUrl = !string.IsNullOrWhiteSpace(cleanVendorPhone)
                ? $"https://web.whatsapp.com/send?phone={cleanVendorPhone}&text={Uri.EscapeDataString(msgVendor)}"
                : $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(msgVendor)}";

            return new
            {
                vehicleNo = alertDev.VehicleNo,
                owner = alertDev.Owner ?? "",
                cellNo = alertDev.CellNo ?? "",
                vendor = vendorName,
                vendorPhone = vendorPhone ?? "",
                expiryStr,
                daysLeft = -daysPast,
                daysPast,
                urgencyClass,
                urgencyLabel,
                waOwnerUrl,
                waVendorUrl,
                isOverdue = true
            };
        }).ToList();

        var alertDevices = expiringWeekAlerts.Select(alertDev =>
        {
            var daysLeft = DateValue.DaysFromNow(alertDev.LatestExpiryDate) ?? 0;
            var urgencyClass = daysLeft == 0 ? "urgent-today" : (daysLeft == 1 ? "urgent-tomorrow" : "urgent-soon");
            var urgencyLabel = daysLeft == 0 ? "Expires Today!" : (daysLeft == 1 ? "Expires Tomorrow" : $"In {daysLeft} days ({DateValue.Short(alertDev.LatestExpiryDate)})");

            var rawPhone = alertDev.CellNo?.Trim() ?? "";
            var cleanPhone = new string(rawPhone.Where(char.IsDigit).ToArray());
            if (cleanPhone.Length == 10) cleanPhone = "91" + cleanPhone;

            var vendorName = alertDev.Vendor?.Trim() ?? "";
            vendorPhoneMap.TryGetValue(vendorName, out var vendorPhone);
            var cleanVendorPhone = new string((vendorPhone ?? "").Where(char.IsDigit).ToArray());
            if (cleanVendorPhone.Length == 10) cleanVendorPhone = "91" + cleanVendorPhone;

            var expiryStr = DateValue.Short(alertDev.LatestExpiryDate);
            var vendorDisp = string.IsNullOrWhiteSpace(vendorName) ? "Vendor" : vendorName;
            var msgOwner = $"*MYGTS SERVICES*\n\u200B\nDear Customer,\n\u200B\nYour *One State One GPS* recharge for vehicle *{alertDev.VehicleNo}* will expire on *{expiryStr}*.\n\u200B\nKindly renew the recharge before the expiry date to avoid interruption of GPS tracking services.\n\u200B\nThank you,\n*MYGTS SERVICES*\n\u200B\n--------------------------------\n\u200B\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*\n\u200B\nಪ್ರಿಯ ಗ್ರಾಹಕರೇ,\n\u200B\nನಿಮ್ಮ ವಾಹನ *{alertDev.VehicleNo}* ನ ಒಂದು ರಾಜ್ಯ ಒಂದು ಜಿಪಿಎಸ್ (One State One GPS) ರಿಚಾರ್ಜ್ *{expiryStr}* ರಂದು ಮುಕ್ತಾಯಗೊಳ್ಳಲಿದೆ.\n\u200B\nಜಿಪಿಎಸ್ ಟ್ರ್ಯಾಕಿಂಗ್ ಸೇವೆಗಳಲ್ಲಿ ಯಾವುದೇ ಅಡಚಣೆಯನ್ನು ತಪ್ಪಿಸಲು ದಯವಿಟ್ಟು ಮುಕ್ತಾಯದ ದಿನಾಂಕದ ಮೊದಲು ರಿಚಾರ್ಜ್ ಅನ್ನು ನವೀಕರಿಸಿ (Renew).\n\u200B\nಧನ್ಯವಾದಗಳು,\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*";
            var msgVendor = $"*MYGTS SERVICES*\n\u200B\nDear {vendorDisp},\n\u200B\nThe GPS recharge for your client's vehicle *{alertDev.VehicleNo}* will expire on *{expiryStr}*.\n\u200B\nPlease ensure the renewal is completed before the expiry date.\n\u200B\nThank you,\n*MYGTS SERVICES*\n\u200B\n--------------------------------\n\u200B\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*\n\u200B\nಪ್ರಿಯ {vendorDisp},\n\u200B\nನಿಮ್ಮ ಕ್ಲೈಂಟ್‌ನ ವಾಹನ *{alertDev.VehicleNo}* ನ ಒಂದು ರಾಜ್ಯ ಒಂದು ಜಿಪಿಎಸ್ (One State One GPS) ರಿಚಾರ್ಜ್ *{expiryStr}* ರಂದು ಮುಕ್ತಾಯಗೊಳ್ಳಲಿದೆ.\n\u200B\nಜಿಪಿಎಸ್ ಟ್ರ್ಯಾಕಿಂಗ್ ಸೇವೆಗಳಲ್ಲಿ ಯಾವುದೇ ಅಡಚಣೆಯನ್ನು ತಪ್ಪಿಸಲು ದಯವಿಟ್ಟು ಮುಕ್ತಾಯದ ದಿನಾಂಕದ ಮೊದಲು ರಿಚಾರ್ಜ್ ಅನ್ನು ನವೀಕರಿಸಿ (Renew).\n\u200B\nಧನ್ಯವಾದಗಳು,\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*";

            var waOwnerUrl = string.IsNullOrWhiteSpace(cleanPhone)
                ? $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(msgOwner)}"
                : $"https://web.whatsapp.com/send?phone={cleanPhone}&text={Uri.EscapeDataString(msgOwner)}";
            var waVendorUrl = !string.IsNullOrWhiteSpace(cleanVendorPhone)
                ? $"https://web.whatsapp.com/send?phone={cleanVendorPhone}&text={Uri.EscapeDataString(msgVendor)}"
                : $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(msgVendor)}";

            return new
            {
                vehicleNo = alertDev.VehicleNo,
                owner = alertDev.Owner ?? "",
                cellNo = alertDev.CellNo ?? "",
                vendor = vendorName,
                vendorPhone = vendorPhone ?? "",
                expiryStr,
                daysLeft,
                urgencyClass,
                urgencyLabel,
                waOwnerUrl,
                waVendorUrl,
                isOverdue = false
            };
        }).ToList();

        var followups = openFollowups.Select(f =>
        {
            var dueDays = DateValue.DaysFromNow(f.DueDate);
            var dueLabel = dueDays == 0 ? "Due Today" : (dueDays < 0 ? $"Overdue by {Math.Abs(dueDays.Value)}d" : (dueDays == 1 ? "Due Tomorrow" : $"Due in {dueDays}d ({f.DueDate})"));
            var urgencyClass = (dueDays <= 0) ? "urgent-today" : (dueDays == 1 ? "urgent-tomorrow" : "urgent-soon");

            return new
            {
                id = f.Id,
                title = f.Title,
                details = f.Details ?? "",
                dueDate = f.DueDate,
                dueLabel,
                urgencyClass,
                targetAudience = f.TargetAudience ?? "@all",
                createdBy = f.CreatedBy ?? ""
            };
        }).ToList();

        var canViewRecharges = User.CanViewModule("Recharges");
        var branchRecharges = canViewRecharges && data.Recharges != null
            ? data.Recharges
                .Where(r => string.Equals(r.Branch, currentBranch, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : new List<RechargeRecord>();

        var pendingRechargeAlerts = branchRecharges
            .Where(r => string.Equals(r.RechargeStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                id = r.Id,
                vehicleNo = r.VehicleNo ?? "",
                imei = r.Imei ?? "",
                iccid = r.Iccid ?? "",
                vendor = r.Vendor ?? "",
                networkOperator = r.NetworkOperator ?? "",
                rechargeDate = r.RechargeDate ?? "",
                createdAt = r.CreatedAt ?? "",
                status = "Pending"
            })
            .ToList();

        var unpaidRechargeAlerts = branchRecharges
            .Where(r => string.Equals(r.RechargeStatus, "Processed", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(r.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.RechargeDate)
            .Select(r =>
            {
                var vNo = r.VehicleNo ?? "";
                var vendorName = r.Vendor?.Trim() ?? "";
                vendorPhoneMap.TryGetValue(vendorName, out var vendorPhone);
                var cleanVendorPhone = new string((vendorPhone ?? "").Where(char.IsDigit).ToArray());
                if (cleanVendorPhone.Length == 10) cleanVendorPhone = "91" + cleanVendorPhone;

                var dev = allFleetDevices.FirstOrDefault(d => !string.IsNullOrEmpty(vNo) && string.Equals(d.VehicleNo?.Trim(), vNo.Trim(), StringComparison.OrdinalIgnoreCase));
                var rawPhone = dev?.CellNo?.Trim() ?? "";
                var cleanPhone = new string(rawPhone.Where(char.IsDigit).ToArray());
                if (cleanPhone.Length == 10) cleanPhone = "91" + cleanPhone;

                var rDate = r.RechargeDate ?? "";
                var msgOwner = $"*MYGTS SERVICES*\n\u200B\nDear Customer,\n\u200B\nThe GPS recharge for vehicle *{vNo}* was processed on *{rDate}*. Kindly clear the pending payment to maintain uninterrupted tracking services.\n\u200B\nThank you,\n*MYGTS SERVICES*";
                var msgVendor = $"*MYGTS SERVICES*\n\u200B\nDear {(string.IsNullOrWhiteSpace(vendorName) ? "Vendor" : vendorName)},\n\u200B\nThe GPS recharge for vehicle *{vNo}* was processed on *{rDate}*. Please clear the pending payment.\n\u200B\nThank you,\n*MYGTS SERVICES*";

                var waOwnerUrl = !string.IsNullOrWhiteSpace(cleanPhone)
                    ? $"https://web.whatsapp.com/send?phone={cleanPhone}&text={Uri.EscapeDataString(msgOwner)}"
                    : $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(msgOwner)}";
                var waVendorUrl = !string.IsNullOrWhiteSpace(cleanVendorPhone)
                    ? $"https://web.whatsapp.com/send?phone={cleanVendorPhone}&text={Uri.EscapeDataString(msgVendor)}"
                    : $"https://web.whatsapp.com/send?text={Uri.EscapeDataString(msgVendor)}";

                return new
                {
                    id = r.Id,
                    vehicleNo = vNo,
                    imei = r.Imei ?? "",
                    iccid = r.Iccid ?? "",
                    vendor = vendorName,
                    vendorPhone = vendorPhone ?? "",
                    owner = dev?.Owner ?? "",
                    cellNo = dev?.CellNo ?? "",
                    networkOperator = r.NetworkOperator ?? "",
                    rechargeDate = rDate,
                    amountPaid = r.AmountPaid,
                    status = "Not Paid",
                    waOwnerUrl,
                    waVendorUrl
                };
            })
            .ToList();

        var distinctVehicles = new HashSet<string>(overdueDeviceItems.Select(d => d.vehicleNo), StringComparer.OrdinalIgnoreCase);
        foreach (var d in alertDevices) distinctVehicles.Add(d.vehicleNo);
        var totalCount = distinctVehicles.Count + lifecycleShifts.Count + followups.Count + pendingRechargeAlerts.Count + unpaidRechargeAlerts.Count;

        return Json(new
        {
            count = totalCount,
            overdueCount = overdueDeviceItems.Count,
            devicesCount = alertDevices.Count,
            shiftsCount = lifecycleShifts.Count,
            followupsCount = followups.Count,
            pendingRechargesCount = pendingRechargeAlerts.Count,
            unpaidRechargesCount = unpaidRechargeAlerts.Count,
            branch = currentBranch,
            isExecutive,
            canViewReminders,
            canEditReminders,
            canViewRecharges,
            overdueDevices = overdueDeviceItems,
            devices = alertDevices,
            lifecycleShifts,
            followups,
            pendingRecharges = pendingRechargeAlerts,
            unpaidRecharges = unpaidRechargeAlerts
        });
    }

    [HttpPost]
    public async Task<IActionResult> CompleteFollowup(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        if (!User.CanEditModule("Reminders")) return Forbid();

        await _store.UpdateAsync(data =>
        {
            var item = data.ManualReminders.FirstOrDefault(r => r.Id == id);
            if (item != null) item.IsComplete = true;
        });

        return Ok(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> MarkInformed([FromForm] string vehicleNo)
    {
        if (string.IsNullOrWhiteSpace(vehicleNo)) return BadRequest();
        var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        await _store.UpdateAsync(data =>
        {
            var dev = data.Devices.FirstOrDefault(d => string.Equals(d.VehicleNo, vehicleNo.Trim(), StringComparison.OrdinalIgnoreCase));
            if (dev != null)
            {
                dev.InformedDate = todayStr;
            }
        });

        return Json(new { success = true, vehicleNo, informedDate = todayStr });
    }

    private static string ResolveVendorPhone(string vendorName, IReadOnlyList<VendorRecord> vendors, IReadOnlyList<DeviceRecord> devices, string branch)
    {
        if (string.IsNullOrWhiteSpace(vendorName)) return "";
        var cleanName = vendorName.Trim();

        // 1. Direct match in Vendors table for current branch, then across all branches
        var v = vendors.FirstOrDefault(x => string.Equals(x.Branch, branch, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase))
             ?? vendors.FirstOrDefault(x => string.Equals(x.Name?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase));

        if (v != null)
        {
            var p = v.EffectiveWhatsAppNumber;
            if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            if (!string.IsNullOrWhiteSpace(v.WhatsAppNumber)) return v.WhatsAppNumber.Trim();
            if (!string.IsNullOrWhiteSpace(v.Phone)) return v.Phone.Trim();
            if (!string.IsNullOrWhiteSpace(v.AltPhone)) return v.AltPhone.Trim();
            if (!string.IsNullOrWhiteSpace(v.ExtraPhones))
            {
                var ep = v.ExtraPhones.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ep)) return ep.Trim();
            }
        }

        // 2. Fallback to most recent CellNo from Devices table for this vendor
        var devCell = devices
            .Where(d => (string.Equals(d.Branch, branch, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(branch))
                        && string.Equals(d.Vendor?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(d.CellNo))
            .OrderByDescending(d => d.LatestExpiryDate)
            .Select(d => d.CellNo!.Trim())
            .FirstOrDefault()
            ?? devices
                .Where(d => string.Equals(d.Vendor?.Trim(), cleanName, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(d.CellNo))
                .OrderByDescending(d => d.LatestExpiryDate)
                .Select(d => d.CellNo!.Trim())
                .FirstOrDefault();

        return devCell ?? "";
    }

    [HttpGet]
    public async Task<IActionResult> GetVendorExpiringVehicles(string vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor)) return BadRequest();
        var data = await _store.SnapshotAsync();
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";
        var vendorClean = vendor.Trim();

        var branchDevices = data.Devices
            .Where(d => string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(d.VehicleNo)
                        && !d.IsNotInUse
                        && string.Equals(d.Vendor?.Trim(), vendorClean, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 1. Devices that are expiring/overdue/suspension (0 to 30 days ahead, or up to 60 days overdue)
        var primaryList = branchDevices
            .Where(d => DateValue.DaysFromNow(d.LatestExpiryDate) is >= 0 and <= 30
                        || (DateValue.DaysFromNow(d.LatestExpiryDate) < 0 && (d.DaysPastExpiry ?? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value) <= 60)
                        || string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => DateValue.DaysFromNow(d.LatestExpiryDate) ?? 9999)
            .ToList();

        // If no urgent expiring devices, return all active devices for this vendor
        var devicesToReturn = (primaryList.Count > 0 ? primaryList : branchDevices.OrderBy(d => d.LatestExpiryDate).Take(50).ToList())
            .Select(d => {
                var daysLeft = DateValue.DaysFromNow(d.LatestExpiryDate) ?? 9999;
                return new
                {
                    vehicleNo = d.VehicleNo,
                    owner = d.Owner ?? "",
                    cellNo = d.CellNo ?? "",
                    expiryDate = DateValue.Short(d.LatestExpiryDate),
                    daysLeft = daysLeft,
                    isExpiringSoon = daysLeft is >= 0 and <= 7,
                    isOverdue = daysLeft < 0,
                    isUrgent = daysLeft is >= 0 and <= 1 || daysLeft < 0,
                    status = !string.IsNullOrWhiteSpace(d.LifecycleStatus) ? d.LifecycleStatus : (!string.IsNullOrWhiteSpace(d.Status) ? d.Status : "Active")
                };
            })
            .ToList();

        var phone = ResolveVendorPhone(vendorClean, data.Vendors, data.Devices, currentBranch);
        var vRec = data.Vendors.FirstOrDefault(v => string.Equals(v.Name.Trim(), vendorClean, StringComparison.OrdinalIgnoreCase));

        return Json(new
        {
            vendor = vendorClean,
            phone = phone,
            altPhone = vRec?.AltPhone ?? "",
            devices = devicesToReturn
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetExpiringVendorsSummary()
    {
        var data = await _store.SnapshotAsync();
        var currentBranch = Request.Cookies["mygts_branch"] == "RTO" ? "RTO" : "DMG";

        var branchDevices = data.Devices
            .Where(d => string.Equals(d.Branch, currentBranch, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(d.VehicleNo)
                        && !d.IsNotInUse
                        && !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Collect all unique vendor names from both Vendors and Devices tables
        var registeredVendors = data.Vendors
            .Where(v => string.Equals(v.Branch, currentBranch, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v.Name))
            .Select(v => v.Name.Trim())
            .ToList();

        var deviceVendors = branchDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.Vendor))
            .Select(d => d.Vendor!.Trim())
            .ToList();

        var allVendorNames = registeredVendors
            .Concat(deviceVendors)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        var result = allVendorNames.Select(name =>
        {
            var phone = ResolveVendorPhone(name, data.Vendors, branchDevices, currentBranch);
            var devList = branchDevices.Where(d => string.Equals(d.Vendor?.Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();
            var expiringCount = devList.Count(d => 
                DateValue.DaysFromNow(d.LatestExpiryDate) is >= 0 and <= 30
                || (DateValue.DaysFromNow(d.LatestExpiryDate) < 0 && (d.DaysPastExpiry ?? -DateValue.DaysFromNow(d.LatestExpiryDate)!.Value) <= 60)
                || string.Equals(d.LifecycleStatus, "Suspension", StringComparison.OrdinalIgnoreCase));

            return new
            {
                vendor = name,
                expiringCount = expiringCount,
                totalCount = devList.Count,
                phone = phone
            };
        })
        .OrderByDescending(v => v.expiringCount > 0 ? 1 : 0)
        .ThenByDescending(v => v.expiringCount)
        .ThenByDescending(v => v.totalCount)
        .ThenBy(v => v.vendor)
        .ToList();

        return Json(result);
    }
}
