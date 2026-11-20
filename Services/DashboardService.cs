using System.Globalization;



using MygtsMvc.Models;



using MygtsMvc.ViewModels;







namespace MygtsMvc.Services;







public sealed class DashboardService



{



    public DashboardViewModel Build(AppData data, string? requestedPeriod, string? activeBranch = "DMG")
    {
        if (!string.IsNullOrWhiteSpace(activeBranch))
        {
            var branchKey = activeBranch.Trim();
            data = new AppData
            {
                Users = data.Users,
                Devices = data.Devices.Where(d => string.Equals(d.Branch, branchKey, StringComparison.OrdinalIgnoreCase)).ToList(),
                Transactions = data.Transactions.Where(t => string.Equals(t.Branch, branchKey, StringComparison.OrdinalIgnoreCase)).ToList(),
                Invoices = data.Invoices.Where(i => string.Equals(i.Branch, branchKey, StringComparison.OrdinalIgnoreCase)).ToList(),
                Config = data.Config.Where(c => string.Equals(c.Branch, branchKey, StringComparison.OrdinalIgnoreCase)).ToList(),
                ManualReminders = data.ManualReminders.Where(r => string.IsNullOrEmpty(r.Branch) || string.Equals(r.Branch, branchKey, StringComparison.OrdinalIgnoreCase)).ToList(),
                Recharges = data.Recharges.Where(r => string.Equals(r.Branch, branchKey, StringComparison.OrdinalIgnoreCase)).ToList(),
                Vendors = data.Vendors.Where(v => string.Equals(v.Branch, branchKey, StringComparison.OrdinalIgnoreCase)).ToList(),
                CustomDropdownOptions = data.CustomDropdownOptions.Where(c => string.Equals(c.Branch, branchKey, StringComparison.OrdinalIgnoreCase)).ToList()
            };
        }

        var config = data.Config.ToDictionary(k => k.Key, k => k.Value, StringComparer.OrdinalIgnoreCase);



        foreach (var d in data.Devices)



        {



            DeviceLifecycleHelper.SyncDevice(d, config);



        }







        var period = NormalizePeriod(requestedPeriod);



        var transactions = data.Transactions



            .Where(transaction => MatchesPeriod(transaction.Date, period))



            .OrderByDescending(transaction => DateValue.Parse(transaction.Date) ?? DateTime.MinValue)



            .ToList();







        var visibleDevices = data.Devices.Where(device => string.Equals(device.StockStatus, "Used", StringComparison.OrdinalIgnoreCase) || string.Equals(device.StockStatus, "Activated", StringComparison.OrdinalIgnoreCase)).ToList();



        



        var activatedVehicles = data.Transactions



            .Where(t => t.PaymentFor == "Activation" && !string.IsNullOrWhiteSpace(t.VehicleNo))



            .Select(t => t.VehicleNo!.Trim())



            .ToHashSet(StringComparer.OrdinalIgnoreCase);







        var inventoryDevices = data.Devices



            .Where(d => string.IsNullOrWhiteSpace(d.VehicleNo) && !string.Equals(d.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase))



            .ToList();







        var fleetDevices = data.Devices



            .Where(device => !string.IsNullOrWhiteSpace(device.VehicleNo) && !string.Equals(device.LifecycleStatus, "Replaced", StringComparison.OrdinalIgnoreCase) && !device.IsNotInUse)



            .ToList();







        var expiringSoon = fleetDevices.Count(device => DateValue.DaysFromNow(device.LatestExpiryDate) is >= 0 and <= 30);



        var unpaidCount = fleetDevices.Count(device =>



            !device.ExcludeFromUnpaid &&



            !string.Equals(device.Paid, "Yes", StringComparison.OrdinalIgnoreCase) &&



            (string.Equals(device.Paid, "No", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(device.InvoiceId) || !device.InvoiceId.StartsWith("old", StringComparison.OrdinalIgnoreCase)) &&



            !data.Transactions.Any(t => string.Equals(t.VehicleNo, device.VehicleNo, StringComparison.OrdinalIgnoreCase)));



        var openReminders = data.ManualReminders.Count(reminder => !reminder.IsComplete);



        var revenue = transactions.Sum(item => item.Amount ?? 0m);



        var profit = transactions.Sum(item => item.Profit ?? 0m);



        var avgTicket = transactions.Count == 0 ? 0 : revenue / transactions.Count;







        var latestInvoice = data.Invoices.OrderByDescending(i => DateValue.Parse(i.Date) ?? DateTime.MinValue).FirstOrDefault();







        var inCulture = CultureInfo.GetCultureInfo("en-IN");



        string FormatCurrency(decimal val) => $"₹{((val % 1m == 0m) ? val.ToString("N0", inCulture) : val.ToString("N2", inCulture))}";







        var metrics = new List<DashboardMetric>



        {



            new("Inventory", inventoryDevices.Count.ToString("N0"), $"{inventoryDevices.Count:N0} inventory rows", "blue", "/Inventory/Index"),



            new("Fleet", fleetDevices.Count.ToString("N0"), $"{fleetDevices.Count:N0} fleet rows", "green", "/Devices/Index"),



            new("Amount", FormatCurrency(revenue), $"{transactions.Count:N0} transactions", "cyan", "/Transactions/Index"),



            new("Gross profit", FormatCurrency(profit), $"{transactions.Count:N0} transactions", "amber", "/Transactions/Index"),



            new("Unpaid vehicles", unpaidCount.ToString("N0"), $"{unpaidCount:N0} unpaid vehicles", "purple", $"/{(!string.IsNullOrWhiteSpace(activeBranch) ? activeBranch : "DMG")}/Devices?Paid=No"),



            new("Expiring in 30d", expiringSoon.ToString("N0"), $"{expiringSoon:N0} expiring in 30d", "red", "/Reminders/Index")



        };







        bool IsRecharge(TransactionRecord t) => string.Equals(t.PaymentFor, "Recharge", StringComparison.OrdinalIgnoreCase) || string.Equals(t.PaymentFor, "Reactivation", StringComparison.OrdinalIgnoreCase);



        var activitySeries = new List<SeriesPoint>();



        var financialSeries = new List<SeriesPoint>();



        var today = DateTime.UtcNow.Date;







        if (period == "Daily")



        {



            var maxHour = DateTime.Now.Hour;



            foreach (var t in transactions)



            {



                if (DateTime.TryParse(t.CreatedAt, out var dt) && dt.ToLocalTime().Hour > maxHour)



                {



                    maxHour = dt.ToLocalTime().Hour;



                }



            }







            for (int i = 0; i <= maxHour; i++)



            {



                var hourTxns = transactions.Where(t =>



                {



                    if (DateTime.TryParse(t.CreatedAt, out var dt))



                    {



                        return dt.ToLocalTime().Hour == i;



                    }



                    return false;



                }).ToList();







                var label = $"{i:D2}:00";



                activitySeries.Add(new SeriesPoint(label, hourTxns.Count(IsRecharge), hourTxns.Sum(t => t.Amount ?? 0m), today.ToString("dd MMM yyyy")));



                financialSeries.Add(new SeriesPoint(label, hourTxns.Sum(t => t.Amount ?? 0m), hourTxns.Sum(t => t.Profit ?? 0m), today.ToString("dd MMM yyyy")));



            }



        }



        else if (period == "Weekly")



        {



            int diff = (7 + (today.DayOfWeek - DayOfWeek.Sunday)) % 7;



            var sunday = today.AddDays(-1 * diff);



            var saturday = sunday.AddDays(6);



            var endDay = today > saturday ? saturday : today;







            for (var d = sunday; d <= endDay; d = d.AddDays(1))



            {



                var bucketDate = d;



                var bucketTxns = transactions.Where(t => DateValue.Parse(t.Date) == bucketDate).ToList();



                var label = bucketDate.ToString("ddd dd");



                activitySeries.Add(new SeriesPoint(label, bucketTxns.Count(IsRecharge), bucketTxns.Sum(t => t.Amount ?? 0m), bucketDate.ToString("dd MMM yyyy")));



                financialSeries.Add(new SeriesPoint(label, bucketTxns.Sum(t => t.Amount ?? 0m), bucketTxns.Sum(t => t.Profit ?? 0m), bucketDate.ToString("dd MMM yyyy")));



            }



        }



        else if (period == "Executive" || period == "Monthly")



        {



            var firstOfMonth = new DateTime(today.Year, today.Month, 1);







            for (var d = firstOfMonth; d <= today; d = d.AddDays(1))



            {



                var bucketDate = d;



                var bucketTxns = transactions.Where(t => DateValue.Parse(t.Date) == bucketDate).ToList();



                var label = bucketDate.ToString("MMM dd");







                activitySeries.Add(new SeriesPoint(label, bucketTxns.Count(IsRecharge), bucketTxns.Sum(t => t.Amount ?? 0m), bucketDate.ToString("dd MMM yyyy")));



                financialSeries.Add(new SeriesPoint(label, bucketTxns.Sum(t => t.Amount ?? 0m), bucketTxns.Sum(t => t.Profit ?? 0m), bucketDate.ToString("dd MMM yyyy")));



            }



        }



        else if (period == "Fiscal Year")



        {



            var fyStart = new DateTime(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1);



            for (int i = 0; i < 12; i++)



            {



                var bucketMonthStart = fyStart.AddMonths(i);



                if (bucketMonthStart > today) break;







                var bucketTxns = transactions.Where(t =>



                {



                    var pd = DateValue.Parse(t.Date);



                    return pd.HasValue && pd.Value.Year == bucketMonthStart.Year && pd.Value.Month == bucketMonthStart.Month;



                }).ToList();







                var label = bucketMonthStart.ToString("MMM yy");



                activitySeries.Add(new SeriesPoint(label, bucketTxns.Count(IsRecharge), bucketTxns.Sum(t => t.Amount ?? 0m), bucketMonthStart.ToString("MMM yy")));



                financialSeries.Add(new SeriesPoint(label, bucketTxns.Sum(t => t.Amount ?? 0m), bucketTxns.Sum(t => t.Profit ?? 0m), bucketMonthStart.ToString("MMM yy")));



            }



        }



        else if (period == "Lifetime")



        {



            var fyGroups = transactions



                .Where(t => DateValue.Parse(t.Date).HasValue)



                .GroupBy(t =>



            {



                var pd = DateValue.Parse(t.Date).GetValueOrDefault();



                int fyStartYear = pd.Month >= 4 ? pd.Year : pd.Year - 1;



                return $"FY {fyStartYear}-{fyStartYear + 1}";



            }).OrderBy(g => g.Key).ToList();







            foreach (var group in fyGroups)



            {



                activitySeries.Add(new SeriesPoint(group.Key, group.Count(IsRecharge), group.Sum(t => t.Amount ?? 0m), group.Key));



                financialSeries.Add(new SeriesPoint(group.Key, group.Sum(t => t.Amount ?? 0m), group.Sum(t => t.Profit ?? 0m), group.Key));



            }



        }



        else



        {



            activitySeries = transactions



                .GroupBy(item => BucketInfo(item.Date, period))



                .OrderBy(group => group.Key.SortKey)



                .Select(group => new SeriesPoint(group.Key.Label, group.Count(IsRecharge), group.Sum(item => item.Amount ?? 0m), group.Key.QueryTerm))



                .TakeLast(12)



                .ToList();







            financialSeries = transactions



                .GroupBy(item => BucketInfo(item.Date, period))



                .OrderBy(group => group.Key.SortKey)



                .Select(group => new SeriesPoint(group.Key.Label, group.Sum(item => item.Amount ?? 0m), group.Sum(item => item.Profit ?? 0m), group.Key.QueryTerm))



                .TakeLast(12)



                .ToList();



        }







        var vendors = data.Transactions



            .Where(item => !string.IsNullOrWhiteSpace(item.Vendor))



            .GroupBy(item => item.Vendor.Trim(), StringComparer.OrdinalIgnoreCase)



            .Select(group =>



            {



                var devicesCount = data.Devices.Count(d => string.Equals(d.Vendor, group.Key, StringComparison.OrdinalIgnoreCase));



                return new VendorMetric(



                    group.Key,



                    group.Sum(item => item.Amount ?? 0m),



                    group.Sum(item => item.Profit ?? 0m),



                    group.Count(),



                    devicesCount



                );



            })



            .OrderByDescending(item => item.Revenue)



            .Take(8)



            .ToList();







        var activeDevices = fleetDevices.Count(IsActiveDevice);







        var fleetStatusDistribution = fleetDevices



            .GroupBy(d => string.IsNullOrWhiteSpace(d.LifecycleStatus) ? "Active" : d.LifecycleStatus)



            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);







        return new DashboardViewModel



        {



            Period = period,



            PeriodLabel = period == "Executive" ? "Executive overview" : $"{period} operating view",



            Metrics = metrics,



            ActivitySeries = activitySeries,



            FinancialSeries = financialSeries,



            TopVendors = vendors,



            TraceDevices = fleetDevices.Take(8).ToList(),



            TraceTransactions = transactions.Take(8).ToList(),



            TotalDevices = fleetDevices.Count,



            ActiveDevices = activeDevices,



            ExpiringSoon = expiringSoon,



            OpenReminders = openReminders,



            LatestBatchUnits = latestInvoice?.Quantity ?? 0,



            LatestBatchDeviceType = latestInvoice?.DeviceType ?? "",



            LatestBatchCost = latestInvoice?.TotalWithTaxes ?? 0,



            FleetStatusDistribution = fleetStatusDistribution



        };



    }







    private static bool IsActiveDevice(DeviceRecord device)



        => string.Equals(device.LifecycleStatus, "Active", StringComparison.OrdinalIgnoreCase);







    private static string NormalizePeriod(string? period)



        => period?.Trim().ToLowerInvariant() switch



        {



            "daily" => "Daily",



            "weekly" => "Weekly",



            "monthly" => "Monthly",



            "fy" or "fiscal year" => "Fiscal Year",



            "lifetime" => "Lifetime",



            _ => "Executive"



        };







    private static bool MatchesPeriod(string? rawDate, string period)



    {



        if (period is "Executive" or "Lifetime") return true;



        var parsed = DateValue.Parse(rawDate);



        if (parsed is null) return false;



        var today = DateTime.UtcNow.Date;



        int diff = (7 + (today.DayOfWeek - DayOfWeek.Sunday)) % 7;



        var sunday = today.AddDays(-1 * diff);



        var saturday = sunday.AddDays(6);







        return period switch



        {



            "Daily" => parsed.Value == today,



            "Weekly" => parsed.Value >= sunday && parsed.Value <= saturday && parsed.Value <= today,



            "Monthly" => parsed.Value.Month == today.Month && parsed.Value.Year == today.Year,



            "Fiscal Year" => parsed.Value >= new DateTime(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1),



            _ => true



        };



    }







    private static (string Label, string SortKey, string QueryTerm) BucketInfo(string? rawDate, string period)



    {



        var parsed = DateValue.Parse(rawDate);



        if (parsed is null) return ("Unknown", "99999999", "");



        return period switch



        {



            "Daily" => (parsed.Value.ToString("HH:00"), parsed.Value.ToString("yyyyMMddHH"), parsed.Value.ToString("dd MMM yyyy")),



            "Weekly" or "Monthly" => (parsed.Value.ToString("MMM dd"), parsed.Value.ToString("yyyyMMdd"), parsed.Value.ToString("dd MMM yyyy")),



            _ => (parsed.Value.ToString("MMM yy", CultureInfo.InvariantCulture), parsed.Value.ToString("yyyyMM"), parsed.Value.ToString("MMM yy"))



        };



    }



}



