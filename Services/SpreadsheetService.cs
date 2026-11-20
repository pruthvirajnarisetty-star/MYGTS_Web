using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MygtsMvc.Models;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Services;

public sealed class SpreadsheetService
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public byte[] Export(AppData data, string? branch = null)
    {
        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);
        var devHeaders = isRto ? DeviceHeadersRto : DeviceHeadersDmg;
        var devValues = isRto ? data.Devices.Select(DeviceValuesRto) : data.Devices.Select(DeviceValuesDmg);
        var txnHeaders = isRto ? TransactionHeadersRto : TransactionHeadersDmg;
        var txnValues = isRto ? data.Transactions.Select(TransactionValuesRto) : data.Transactions.Select(TransactionValuesDmg);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            WriteXml(archive, "[Content_Types].xml", ContentTypes());
            WriteXml(archive, "_rels/.rels", Relationships());
            WriteXml(archive, "xl/workbook.xml", Workbook(data));
            WriteXml(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships(data));
            WriteXml(archive, "xl/styles.xml", FormattedSampleStyles());
            WriteXml(archive, "xl/worksheets/sheet1.xml", FormattedSampleSheet(devHeaders, devValues));
            WriteXml(archive, "xl/worksheets/sheet2.xml", FormattedSampleSheet(txnHeaders, txnValues));
            WriteXml(archive, "xl/worksheets/sheet3.xml", FormattedSampleSheet(InvoiceHeadersClean, data.Invoices.Select(InvoiceValuesClean)));
            WriteXml(archive, "xl/worksheets/sheet4.xml", FormattedSampleSheet(ConfigHeaders, data.Config.Select(ConfigValues)));
        }
        return output.ToArray();
    }

    public byte[] ExportDevices(IEnumerable<DeviceRecord> devices, string? branch = null)
    {
        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);
        var headers = isRto ? DeviceHeadersRto : DeviceHeadersDmg;
        var rows = devices.Select(d => isRto ? DeviceValuesRto(d) : DeviceValuesDmg(d));
        return CreateFormattedSampleWorkbook(isRto ? "Devices_RTO" : "Devices_DMG", headers, rows);
    }

    public byte[] ExportDevicesM2m(IEnumerable<DeviceRecord> devices)
    {
        var rows = devices.Select((d, idx) => new[]
        {
            (idx + 1).ToString(),
            d.Iccid ?? "",
            d.Imei ?? "",
            !string.IsNullOrWhiteSpace(d.DeviceSerialNo) ? d.DeviceSerialNo : (!string.IsNullOrWhiteSpace(d.Imei) ? d.Imei : (d.SlNo ?? "")),
            "AQUILA",
            "BHARAT 101",
            d.EngineNo ?? "",
            d.ChasisNo ?? "",
            d.VehicleNo ?? "",
            d.Owner ?? "",
            d.CellNo ?? "",
            d.Address ?? "",
            string.IsNullOrWhiteSpace(d.ProofOfAddress) ? "Aadhar" : d.ProofOfAddress,
            d.DocumentNumber ?? "",
            string.IsNullOrWhiteSpace(d.ProofOfAddress) ? "Aadhar" : d.ProofOfAddress,
            d.DocumentNumber ?? "",
            "103.138.197.90",
            "13.127.228.11",
            "w.aquilatrack.com",
            "9758458458",
            "9035163862",
            "9563958958"
        });
        return ExportToXls("M2M_Activation", DeviceHeadersM2m, rows);
    }

    public byte[] ExportDevicesKycCl(IEnumerable<DeviceRecord> devices)
    {
        var rows = devices.Select((d, idx) => new[]
        {
            (idx + 1).ToString(),
            d.Imei ?? "",
            d.EngineNo ?? "",
            d.ChasisNo ?? "",
            NormalizeType(d.Type),
            d.VehicleMake ?? "",
            d.VehicleModel ?? "",
            d.Owner ?? "",
            d.CellNo ?? "",
            "KA",
            NormalizeRto(d.Rto, d.VehicleNo),
            d.Address ?? "",
            string.IsNullOrWhiteSpace(d.ProofOfAddress) ? "AADHAR" : d.ProofOfAddress.ToUpperInvariant(),
            d.DocumentNumber ?? "",
            string.IsNullOrWhiteSpace(d.ProofOfAddress) ? "AADHAR" : d.ProofOfAddress.ToUpperInvariant(),
            d.DocumentNumber ?? "",
            d.VehicleNo ?? ""
        });
        return ExportToXls("KYC_CL", DeviceHeadersKycCl, rows);
    }

    public static string NormalizeRto(string? rto, string? vehicleNo = null)
    {
        if (!string.IsNullOrWhiteSpace(rto))
        {
            var clean = rto.Trim().ToUpperInvariant().Replace(" ", "").Replace("_", "-");
            var match = System.Text.RegularExpressions.Regex.Match(clean, @"^KA-?(\d{1,2})$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var num) && num >= 1 && num <= 71)
            {
                return $"KA-{num:D2}";
            }
            return clean.Replace(" ", "");
        }

        if (!string.IsNullOrWhiteSpace(vehicleNo))
        {
            var cleanVeh = System.Text.RegularExpressions.Regex.Replace(vehicleNo, @"[^A-Za-z0-9]", "").ToUpperInvariant();
            var vehMatch = System.Text.RegularExpressions.Regex.Match(cleanVeh, @"^KA(\d{1,2})");
            if (vehMatch.Success && int.TryParse(vehMatch.Groups[1].Value, out var vNum) && vNum >= 1 && vNum <= 71)
            {
                return $"KA-{vNum:D2}";
            }
        }

        return "";
    }

    public static string NormalizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "NEW";
        var t = type.Trim().ToUpperInvariant();
        if (t.Contains("2") || t == "NEW") return "NEW";
        if (t.Contains("1") || t == "OLD") return "OLD";
        return t;
    }

    public static string NormalizeDeviceName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var trimmed = name.Trim();
        if (string.Equals(trimmed, "Wetrack140", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "WeTrack", StringComparison.OrdinalIgnoreCase))
        {
            return "WeTrack";
        }
        return trimmed;
    }

    public static string NormalizeExtras(string? extras)
    {
        if (string.IsNullOrWhiteSpace(extras) || extras.Trim() == "0" || string.Equals(extras.Trim(), "none", StringComparison.OrdinalIgnoreCase))
            return "None";
        return extras.Trim();
    }


    public byte[] ExportTransactions(IEnumerable<TransactionRecord> transactions, string? branch = null)
    {
        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);
        var headers = isRto ? TransactionHeadersRto : TransactionHeadersDmg;
        var rows = transactions.Select(t => isRto ? TransactionValuesRto(t) : TransactionValuesDmg(t));
        return CreateFormattedSampleWorkbook(isRto ? "Transactions_RTO" : "Transactions_DMG", headers, rows);
    }

    public byte[] ExportInvoices(IEnumerable<InvoiceRecord> invoices, string? branch = null)
    {
        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);
        return CreateFormattedSampleWorkbook(isRto ? "Invoices_RTO" : "Invoices_DMG", InvoiceHeadersClean, invoices.Select(InvoiceValuesClean));
    }

    public byte[] ExportCompanyRechargeBatch(CompanyRechargeBatch batch, IEnumerable<CompanyRechargeItem> items)
    {
        var headers = new[]
        {
            "Sl.no", "Vehicle No", "IMEI", "ICICID", "Pay Reference No", "Request ID", "Action Type", "Per Device"
        };
        var rows = items.OrderBy(i => i.SlNo).Select(i => new[]
        {
            i.SlNo.ToString(),
            i.VehicleNo,
            i.Imei,
            i.Iccid,
            batch.PayReferenceNo,
            i.RequestId,
            i.ActionType,
            i.PerDeviceAmount.ToString("0.00")
        });

        var sheetName = !string.IsNullOrWhiteSpace(batch.PoNumber) ? batch.PoNumber.Replace("/", "-").Replace("\\", "-") : "Batch";
        if (sheetName.Length > 28) sheetName = sheetName.Substring(0, 28);
        return CreateFormattedSampleWorkbook(sheetName, headers, rows);
    }

    public byte[] ExportVendorExpiringVehicles(string vendorName, IEnumerable<DeviceRecord> devices)
    {
        var headers = new[]
        {
            "Sl.No",
            "Vehicle Number",
            "IMEI",
            "Latest Expiry Date",
            "Status",
            "Days Left"
        };

        var rows = devices.Select((d, idx) =>
        {
            var daysLeft = DateValue.DaysFromNow(d.LatestExpiryDate);
            var daysLabel = daysLeft.HasValue
                ? (daysLeft.Value == 0 ? "Expires Today" : (daysLeft.Value < 0 ? $"Expired ({Math.Abs(daysLeft.Value)}d ago)" : $"{daysLeft.Value} days"))
                : "";

            var status = string.IsNullOrWhiteSpace(d.LifecycleStatus)
                ? (string.IsNullOrWhiteSpace(d.Status) ? "Active" : d.Status)
                : d.LifecycleStatus;

            return new[]
            {
                (idx + 1).ToString(),
                d.VehicleNo ?? "",
                d.Imei ?? "",
                DateValue.Short(d.LatestExpiryDate) ?? (d.LatestExpiryDate ?? ""),
                status,
                daysLabel
            };
        });

        var safeName = System.Text.RegularExpressions.Regex.Replace(vendorName ?? "Vendor", @"[:\\/?*\[\]]", "-").Trim();
        var sheetTitle = $"Expiring - {safeName}";
        if (sheetTitle.Length > 31) sheetTitle = sheetTitle.Substring(0, 31).Trim();
        if (string.IsNullOrWhiteSpace(sheetTitle)) sheetTitle = "Expiring Vehicles";

        return CreateFormattedSampleWorkbook(sheetTitle, headers, rows);
    }

    public byte[] ExportInvoicesWithDevices(IEnumerable<InvoiceRecord> invoices, IEnumerable<DeviceRecord> allDevices, string? branch = null)
    {
        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);
        var invoiceList = invoices.ToList();
        var deviceList = allDevices.ToList();

        var sheets = new List<MultiSheetItem>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Primary Invoices sheet
        var mainSheetName = isRto ? "Invoices_RTO" : "Invoices_DMG";
        usedNames.Add(mainSheetName);
        sheets.Add(new MultiSheetItem
        {
            SheetName = mainSheetName,
            Headers = InvoiceHeadersClean,
            Rows = invoiceList.Select(InvoiceValuesClean)
        });

        // 2. Individual sheets per invoice named with the invoice ID/number
        var deviceHeaders = isRto ? DeviceHeadersRto : DeviceHeadersDmg;
        foreach (var inv in invoiceList)
        {
            var invDevices = deviceList
                .Where(d => string.Equals(d.InvoiceId, inv.InvoiceNo, StringComparison.OrdinalIgnoreCase) ||
                            (BatchColorHelper.RtoInvoiceToBatchMap.TryGetValue(inv.InvoiceNo, out var bName) && string.Equals(d.InvoiceId, bName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var sheetName = SanitizeSheetName(inv.InvoiceNo, usedNames);
            sheets.Add(new MultiSheetItem
            {
                SheetName = sheetName,
                Headers = deviceHeaders,
                Rows = invDevices.Select(d => isRto ? DeviceValuesRto(d) : DeviceValuesDmg(d))
            });
        }

        return CreateMultiSheetWorkbook(sheets);
    }

    public byte[] ExportInventoryAllBatches(IEnumerable<InvoiceGroup> batches, string? branch = null)
    {
        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);
        var deviceHeaders = isRto ? DeviceHeadersRto : DeviceHeadersDmg;
        var sheets = new List<MultiSheetItem>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var batchList = batches.ToList();
        var allDevices = batchList.SelectMany(b => b.Devices).ToList();

        // 1. Consolidated master sheet "All_Inventory"
        sheets.Add(new MultiSheetItem
        {
            SheetName = SanitizeSheetName("All_Inventory", usedNames),
            Headers = deviceHeaders,
            Rows = allDevices.Select(d => isRto ? DeviceValuesRto(d) : DeviceValuesDmg(d))
        });

        // 2. Individual sheets per batch
        foreach (var batch in batchList)
        {
            var sheetName = SanitizeSheetName(batch.InvoiceNo, usedNames);
            sheets.Add(new MultiSheetItem
            {
                SheetName = sheetName,
                Headers = deviceHeaders,
                Rows = batch.Devices.Select(d => isRto ? DeviceValuesRto(d) : DeviceValuesDmg(d))
            });
        }

        return CreateMultiSheetWorkbook(sheets);
    }

    public byte[] ExportInventoryBatch(InvoiceGroup batch, IEnumerable<string>? selectedDeviceIds = null, string? branch = null)
    {
        var isRto = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase);
        var deviceHeaders = isRto ? DeviceHeadersRto : DeviceHeadersDmg;
        var devices = batch.Devices;
        if (selectedDeviceIds != null)
        {
            var idSet = new HashSet<string>(selectedDeviceIds, StringComparer.OrdinalIgnoreCase);
            if (idSet.Count > 0)
            {
                devices = devices.Where(d => idSet.Contains(d.Id)).ToList();
            }
        }
        var sheetName = SanitizeSheetName(string.IsNullOrWhiteSpace(batch.InvoiceNo) ? "Batch" : batch.InvoiceNo, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return CreateFormattedSampleWorkbook(sheetName, deviceHeaders, devices.Select(d => isRto ? DeviceValuesRto(d) : DeviceValuesDmg(d)));
    }

    public byte[] ExportTaisysBulkFormat(IEnumerable<DeviceRecord> devices)
    {
        var items = devices.Select(d => new { iccid = d.Iccid ?? "", imei = d.Imei ?? "" }).ToList();
        var tempInput = Path.Combine(Path.GetTempPath(), $"taisys_in_{Guid.NewGuid():N}.json");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"taisys_out_{Guid.NewGuid():N}.xlsx");
        try
        {
            File.WriteAllText(tempInput, JsonSerializer.Serialize(items));
            var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Services", "generate_taisys_bulk.py");
            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(AppContext.BaseDirectory, "Services", "generate_taisys_bulk.py");
            }
            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Services", "generate_taisys_bulk.py");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" -i \"{tempInput}\" -o \"{tempOutput}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(15000);
            }
            if (File.Exists(tempOutput))
            {
                return File.ReadAllBytes(tempOutput);
            }
        }
        catch
        {
            // fallback
        }
        finally
        {
            try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }
            try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
        }
        return Array.Empty<byte>();
    }

    public byte[] ExportToXls(string sheetName, string[] headers, IEnumerable<string[]> rows)
    {
        var payload = new
        {
            sheetName,
            headers,
            rows = rows.ToList()
        };
        var tempInput = Path.Combine(Path.GetTempPath(), $"xls_in_{Guid.NewGuid():N}.json");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"xls_out_{Guid.NewGuid():N}.xls");
        try
        {
            File.WriteAllText(tempInput, JsonSerializer.Serialize(payload));
            var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Services", "generate_xls.py");
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" -i \"{tempInput}\" -o \"{tempOutput}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit(15000);
            }
            if (File.Exists(tempOutput))
            {
                return File.ReadAllBytes(tempOutput);
            }
        }
        catch
        {
            // fallback
        }
        finally
        {
            try { if (File.Exists(tempInput)) File.Delete(tempInput); } catch { }
            try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
        }
        return Array.Empty<byte>();
    }

    public AppData Import(Stream input)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        var sharedStrings = ReadSharedStrings(archive);
        var data = new AppData();
        data.Devices = MapDevices(ReadRows(archive, "xl/worksheets/sheet1.xml", sharedStrings));
        data.Transactions = MapTransactions(ReadRows(archive, "xl/worksheets/sheet2.xml", sharedStrings));
        data.Invoices = MapInvoices(ReadRows(archive, "xl/worksheets/sheet3.xml", sharedStrings));
        data.Config = MapConfig(ReadRows(archive, "xl/worksheets/sheet4.xml", sharedStrings));
        return data;
    }

    public static readonly string[] DeviceHeadersDmg = ["sl_no", "vehicle_no", "iccid", "imei", "sim_airtel", "sim_bsnl", "status", "type", "device_name", "invoice_id", "vendor", "owner", "cell_no", "activation_date", "latest_expiry_date", "engine_no", "chasis_no", "proof_of_address", "document_number", "notes", "days_past_expiry", "lifecycle_status", "stock_status"];
    public static readonly string[] DeviceHeadersRto = ["sl_no", "vehicle_no", "iccid", "imei", "device_serial_no", "sim_airtel", "sim_bsnl", "status", "device_name", "type", "registration_date", "latest_expiry_date", "vendor", "owner", "cell_no", "activation_date", "engine_no", "chasis_no", "vehicle_make", "vehicle_model", "rto", "address", "proof_of_address", "document_number", "invoice_id", "notes", "days_past_expiry", "lifecycle_status", "stock_status", "cdac_certificate", "vldt_certificate"];
    public static readonly string[] DeviceHeadersM2m = ["SR_NO", "ICCID*", "DEVICE_IMEI*", "DEVICE_SR.NO*", "DEVICE_MAKE*", "DEVICE_MODEL*", "ENGINE_NO*", "CHASSIS_NO*", "VIN/REG_NUMBER*", "NAME*", "Registered Mobile no. (RMN)*", "Address*", "PROOF OF ADDRESS*", "POA DOC NO.", "PROOF OF IDENTITY*", "POI DOC NO.", "IP ADDRESS 1", "IP ADDRESS 2", "IP ADDRESS 3", "MOBILE NUMBER 1", "MOBILE NUMBER 2", "MOBILE NUMBER 3"];
    public static readonly string[] DeviceHeadersKycCl = ["SR_NO", "DEVICE_IMEI*", "ENGINE_NO*", "CHASSIS_NO*", "Vehicle type Old/New*", "Vehicle Make/Manufacturer*", "Vehicle Model*", "End_Customer_NAME*", "Registerd Mobile no. (RMN)*", "RTO State", "RTO No.(In which RTO vehicle go for registration)*", "Address*", "PROOF OF ADDRESS*", "POA No*", "PROOF OF IDENTITY", "POI No", "Vehicle No*"];
    public static readonly string[] TransactionHeadersDmg = ["sl_no", "date", "vehicle_no", "vendor", "payment_mode", "payment_for", "amount", "batch_id", "utr", "paid_to", "extras", "activation_date", "new_expiry_date", "profit", "notes", "transaction_type", "individual_utrs", "bulk_allocations", "payment_breakdown"];
    public static readonly string[] TransactionHeadersRto = ["sl_no", "date", "vehicle_no", "vendor", "payment_mode", "payment_for", "amount", "batch_id", "utr", "paid_to", "activation_date", "new_expiry_date", "profit", "notes", "transaction_type", "individual_utrs", "bulk_allocations", "payment_breakdown"];
    public static readonly string[] InvoiceHeadersClean = ["date", "device_type", "network_operator", "invoice_no", "quantity", "rate", "courier_charges", "panic_button_quantity", "panic_button_cost", "panic_button_total", "amount", "round_off", "total_with_taxes", "cost_per_device", "notes"];
    public static readonly string[] ConfigHeaders = ["KEY", "VALUE"];

    private static string[] DeviceValuesDmg(DeviceRecord x) => [x.SlNo.ToString(), x.VehicleNo, x.Iccid, x.Imei, x.SimAirtel, x.SimBsnl, x.Status, NormalizeType(x.Type), x.DeviceName, x.InvoiceId, x.Vendor, x.Owner, x.CellNo, x.ActivationDate, x.LatestExpiryDate, x.EngineNo, x.ChasisNo, x.ProofOfAddress, x.DocumentNumber, x.Notes, x.DaysPastExpiry?.ToString() ?? "0", x.LifecycleStatus, x.StockStatus];
    private static string[] DeviceValuesRto(DeviceRecord x) => [x.SlNo.ToString(), x.VehicleNo, x.Iccid, x.Imei, x.DeviceSerialNo, x.SimAirtel, x.SimBsnl, x.Status, x.DeviceName, NormalizeType(x.Type), x.RegistrationDate, x.LatestExpiryDate, x.Vendor, x.Owner, x.CellNo, x.ActivationDate, x.EngineNo, x.ChasisNo, x.VehicleMake, x.VehicleModel, NormalizeRto(x.Rto, x.VehicleNo), x.Address, x.ProofOfAddress, x.DocumentNumber, x.InvoiceId, x.Notes, x.DaysPastExpiry?.ToString() ?? "0", x.LifecycleStatus, x.StockStatus, x.CdacCertificate ?? "NO", x.VldtCertificate ?? "NO"];
    
    private static string GetTransactionType(TransactionRecord x)
    {
        if ((!string.IsNullOrWhiteSpace(x.PaymentFor) && x.PaymentFor.Equals("Bulk allocation", StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(x.Notes) && x.Notes.Contains("[BULK_PARTS:")))
            return "Bulk Split";
        if ((!string.IsNullOrWhiteSpace(x.Notes) && (x.Notes.Contains("[MERGE_PARTS:") || x.Notes.Contains("Split Payment:"))) ||
            (!string.IsNullOrWhiteSpace(x.PaymentMode) && x.PaymentMode.Contains('+')))
            return "Merge Payment";
        return "Standard";
    }

    private static string GetBulkAllocationsSummary(TransactionRecord x)
    {
        if (string.IsNullOrWhiteSpace(x.Notes) || !x.Notes.Contains("[BULK_PARTS:")) return "";
        try
        {
            var start = x.Notes.IndexOf("[BULK_PARTS:") + 12;
            var end = x.Notes.IndexOf("]", start);
            if (start >= 12 && end > start)
            {
                var json = x.Notes.Substring(start, end - start);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var items = new List<string>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var v = el.TryGetProperty("vehicleNo", out var vP) ? vP.GetString() : "";
                    var a = el.TryGetProperty("amount", out var aP) ? aP.GetDecimal() : 0m;
                    items.Add($"{v} (₹{a:N2})");
                }
                return string.Join(" | ", items);
            }
        }
        catch {}
        return "";
    }

    private static string GetPaymentBreakdownSummary(TransactionRecord x)
    {
        if (string.IsNullOrWhiteSpace(x.Notes)) return "";
        try
        {
            if (x.Notes.Contains("[BULK_PAYMENTS:"))
            {
                var start = x.Notes.IndexOf("[BULK_PAYMENTS:") + 15;
                var end = x.Notes.IndexOf("]", start);
                if (start >= 15 && end > start)
                {
                    var json = x.Notes.Substring(start, end - start);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var items = new List<string>();
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var m = el.TryGetProperty("mode", out var mP) ? mP.GetString() : "";
                        var a = el.TryGetProperty("amount", out var aP) ? aP.GetDecimal() : 0m;
                        var u = el.TryGetProperty("utr", out var uP) ? uP.GetString() : "";
                        var pt = el.TryGetProperty("paidTo", out var ptP) ? ptP.GetString() : "";
                        items.Add($"{m}: ₹{a:N2} (UTR: {u}{(string.IsNullOrWhiteSpace(pt) ? "" : ", To: " + pt)})");
                    }
                    return string.Join(" | ", items);
                }
            }
            else if (x.Notes.Contains("[MERGE_PARTS:"))
            {
                var start = x.Notes.IndexOf("[MERGE_PARTS:") + 13;
                var end = x.Notes.IndexOf("]", start);
                if (start >= 13 && end > start)
                {
                    var json = x.Notes.Substring(start, end - start);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var items = new List<string>();
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        var m = el.TryGetProperty("mode", out var mP) ? mP.GetString() : "";
                        var a = el.TryGetProperty("amount", out var aP) ? aP.GetDecimal() : 0m;
                        var u = el.TryGetProperty("utr", out var uP) ? uP.GetString() : "";
                        items.Add($"{m}: ₹{a:N2} (UTR: {u})");
                    }
                    return string.Join(" | ", items);
                }
            }
        }
        catch {}
        return "";
    }

    private static string[] TransactionValuesDmg(TransactionRecord x) => [x.SlNo.ToString(), x.Date, x.VehicleNo, x.Vendor, x.PaymentMode, x.PaymentFor, x.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", x.BatchId ?? "", x.Utr, x.PaidTo, NormalizeExtras(x.Extras), x.ActivationDate, x.NewExpiryDate, x.Profit?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", x.Notes, GetTransactionType(x), x.Utr, GetBulkAllocationsSummary(x), GetPaymentBreakdownSummary(x)];
    private static string[] TransactionValuesRto(TransactionRecord x) => [x.SlNo.ToString(), x.Date, x.VehicleNo, x.Vendor, x.PaymentMode, x.PaymentFor, x.Amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", x.BatchId ?? "", x.Utr, x.PaidTo, x.ActivationDate, x.NewExpiryDate, x.Profit?.ToString("0.00", CultureInfo.InvariantCulture) ?? "", x.Notes, GetTransactionType(x), x.Utr, GetBulkAllocationsSummary(x), GetPaymentBreakdownSummary(x)];
    private static string[] InvoiceValuesClean(InvoiceRecord x) => [x.Date, x.DeviceType, x.NetworkOperator, x.InvoiceNo, x.Quantity.ToString(), x.Rate.ToString("0.00", CultureInfo.InvariantCulture), x.CourierCharges.ToString("0.00", CultureInfo.InvariantCulture), x.PanicButtonQuantity.ToString(), x.PanicButtonCost.ToString("0.00", CultureInfo.InvariantCulture), x.PanicButtonTotal.ToString("0.00", CultureInfo.InvariantCulture), x.Amount.ToString("0.00", CultureInfo.InvariantCulture), x.RoundOff.ToString("0.00", CultureInfo.InvariantCulture), x.TotalWithTaxes.ToString("0.00", CultureInfo.InvariantCulture), x.CostPerDevice.ToString("0.00", CultureInfo.InvariantCulture), x.Notes];
    private static string[] ConfigValues(ConfigItem x) => [x.Key, x.Value];

    private static IReadOnlyList<Dictionary<string, string>> ReadRows(ZipArchive archive, string path, IReadOnlyList<string> sharedStrings)
    {
        var entry = archive.GetEntry(path);
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var rows = document.Descendants(MainNs + "row").Select(row =>
        {
            var cells = row.Elements(MainNs + "c").Select(cell =>
            {
                var reference = (string?)cell.Attribute("r") ?? "A1";
                var column = ColumnIndex(reference);
                var type = (string?)cell.Attribute("t");
                var value = type == "inlineStr"
                    ? cell.Descendants(MainNs + "t").FirstOrDefault()?.Value ?? ""
                    : cell.Element(MainNs + "v")?.Value ?? "";
                if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count) value = sharedStrings[index];
                if (!string.IsNullOrWhiteSpace(value) && (value.Contains('E') || value.Contains('e')) && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decVal))
                {
                    value = decVal.ToString("0", CultureInfo.InvariantCulture);
                }
                return (column, value);
            });
            return cells.ToDictionary(item => item.column, item => item.value);
        }).ToList();
        if (rows.Count == 0) return [];
        var headerRow = rows[0];
        var validHeaders = headerRow
            .Where(h => !string.IsNullOrWhiteSpace(h.Value))
            .GroupBy(h => h.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Column: g.First().Key, Header: g.Key))
            .ToList();

        return rows.Skip(1).Select(row =>
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, header) in validHeaders)
            {
                dict[header] = row.TryGetValue(col, out var val) ? val.Trim() : "";
            }
            return dict;
        }).Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value))).ToList();
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(MainNs + "si").Select(item => string.Concat(item.Descendants(MainNs + "t").Select(text => text.Value))).ToList();
    }

    private static List<DeviceRecord> MapDevices(IReadOnlyList<Dictionary<string, string>> rows) => rows.Select(row => new DeviceRecord
    {
        Id = Get(row, "id") ?? Guid.NewGuid().ToString("N"),
        SlNo = Get(row, "sl_no") ?? "",
        VehicleNo = Get(row, "vehicle_no") ?? "",
        Iccid = Get(row, "iccid") ?? "",
        Imei = Get(row, "imei") ?? "",
        DeviceSerialNo = Get(row, "device_serial_no") ?? Get(row, "deviceserialno") ?? Get(row, "device_serial") ?? "",
        SimAirtel = Get(row, "sim_airtel") ?? "",
        SimBsnl = Get(row, "sim_bsnl") ?? "",
        Status = Get(row, "status") ?? "Active",
        Type = Get(row, "type") ?? "",
        DeviceName = Get(row, "device_name") ?? "",
        InvoiceId = Get(row, "invoice_id") ?? "",
        Vendor = Get(row, "vendor") ?? "",
        Owner = Get(row, "owner") ?? "",
        CellNo = Get(row, "cell_no") ?? "",
        RegistrationDate = Get(row, "registration_date") ?? "",
        ActivationDate = Get(row, "activation_date") ?? "",
        LatestExpiryDate = Get(row, "latest_expiry_date") ?? "",
        EngineNo = Get(row, "engine_no") ?? "",
        ChasisNo = Get(row, "chasis_no") ?? "",
        ProofOfAddress = Get(row, "proof_of_address") ?? "",
        DocumentNumber = Get(row, "document_number") ?? "",
        Notes = Get(row, "notes") ?? "",
        CreatedAt = Get(row, "created_at") ?? DateTime.UtcNow.ToString("O"),
        DaysPastExpiry = GetNullableInt(row, "days_past_expiry"),
        LifecycleStatus = Get(row, "lifecycle_status") ?? "Active",
        StockStatus = Get(row, "stock_status") ?? "Used",
        Branch = Get(row, "branch") ?? "DMG"
    }).ToList();

    private static List<TransactionRecord> MapTransactions(IReadOnlyList<Dictionary<string, string>> rows) => rows.Select(row => new TransactionRecord
    {
        Id = Get(row, "id") ?? Guid.NewGuid().ToString("N"),
        SlNo = Get(row, "sl_no") ?? "",
        Date = Get(row, "date") ?? "",
        VehicleNo = Get(row, "vehicle_no") ?? "",
        Vendor = Get(row, "vendor") ?? "",
        PaymentMode = Get(row, "payment_mode") ?? "UPI",
        PaymentFor = Get(row, "payment_for") ?? "Recharge",
        Amount = GetDecimal(row, "amount"),
        BatchId = Get(row, "batch_id") ?? "",
        Utr = Get(row, "utr") ?? "",
        PaidTo = Get(row, "paid_to") ?? "",
        Extras = NormalizeExtras(Get(row, "extras")),
        ActivationDate = Get(row, "activation_date") ?? "",
        NewExpiryDate = Get(row, "new_expiry_date") ?? "",
        Notes = Get(row, "notes") ?? "",
        CreatedAt = Get(row, "created_at") ?? DateTime.UtcNow.ToString("O"),
        ExcludingDmg = GetDecimal(row, "excluding_dmg"),
        Profit = GetDecimal(row, "profit")
    }).ToList();

    private static List<InvoiceRecord> MapInvoices(IReadOnlyList<Dictionary<string, string>> rows) => rows.Select(row => new InvoiceRecord
    {
        Id = Get(row, "id") ?? Guid.NewGuid().ToString("N"),
        Date = Get(row, "date") ?? "",
        DeviceType = Get(row, "device_type") ?? "GPS",
        NetworkOperator = Get(row, "network_operator", "networkoperator", "operator") ?? "",
        InvoiceNo = Get(row, "invoice_no") ?? "",
        AckNo = Get(row, "ack_no") ?? "",
        EwayBillNumber = Get(row, "eway_bill_number") ?? "",
        Quantity = GetInt(row, "quantity"),
        Rate = GetDecimal(row, "rate", "device_cost"),
        CourierCharges = GetDecimal(row, "courier_charges", "extra_charges"),
        PanicButtonQuantity = GetInt(row, "panic_button_quantity"),
        PanicButtonCost = GetDecimal(row, "panic_button_cost"),
        PanicButtonTotal = GetDecimal(row, "panic_button_total"),
        Amount = GetDecimal(row, "amount", "base_amount"),
        RoundOff = GetDecimal(row, "round_off"),
        TotalWithTaxes = GetDecimal(row, "total_with_taxes", "total"),
        CostPerDevice = GetDecimal(row, "cost_per_device"),
        Notes = Get(row, "notes") ?? "",
        CreatedAt = Get(row, "created_at") ?? DateTime.UtcNow.ToString("O")
    }).ToList();

    private static List<ConfigItem> MapConfig(IReadOnlyList<Dictionary<string, string>> rows) => rows.Select(row => new ConfigItem { Key = Get(row, "KEY", "key") ?? "", Value = Get(row, "VALUE", "value") ?? "" }).Where(item => !string.IsNullOrWhiteSpace(item.Key)).ToList();

    private static string? Get(Dictionary<string, string> row, params string[] keys) => keys.Select(key => row.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)).Value).FirstOrDefault(value => value is not null);
    private static int GetInt(Dictionary<string, string> row, params string[] keys) => int.TryParse(Get(row, keys), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    private static int? GetNullableInt(Dictionary<string, string> row, params string[] keys) => int.TryParse(Get(row, keys), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static decimal GetDecimal(Dictionary<string, string> row, params string[] keys) => decimal.TryParse(Get(row, keys), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static int ColumnIndex(string reference)
    {
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var result = 0;
        foreach (var letter in letters) result = result * 26 + letter - 'A' + 1;
        return result - 1;
    }

    private static XDocument ContentTypes()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        return new XDocument(new XElement(ns + "Types",
            new XElement(ns + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ns + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ns + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(ns + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
            Enumerable.Range(1, 4).Select(i => new XElement(ns + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
    }

    private static XDocument SingleSheetContentTypes()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        return new XDocument(new XElement(ns + "Types",
            new XElement(ns + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ns + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ns + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(ns + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
            new XElement(ns + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
    }

    private static XDocument Relationships()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(new XElement(ns + "Relationships", new XElement(ns + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
    }

    private static XDocument Workbook(AppData data) => new(new XElement(MainNs + "workbook", new XAttribute(XNamespace.Xmlns + "r", RelNs), new XElement(MainNs + "sheets", new XElement(MainNs + "sheet", new XAttribute("name", "Sheet1_Devices"), new XAttribute("sheetId", 1), new XAttribute(RelNs + "id", "rId1")), new XElement(MainNs + "sheet", new XAttribute("name", "Sheet2_Transactions"), new XAttribute("sheetId", 2), new XAttribute(RelNs + "id", "rId2")), new XElement(MainNs + "sheet", new XAttribute("name", "Invoices"), new XAttribute("sheetId", 3), new XAttribute(RelNs + "id", "rId3")), new XElement(MainNs + "sheet", new XAttribute("name", "Config"), new XAttribute("sheetId", 4), new XAttribute(RelNs + "id", "rId4")))));

    private static XDocument SingleSheetWorkbook(string sheetName) => new(new XElement(MainNs + "workbook", new XAttribute(XNamespace.Xmlns + "r", RelNs), new XElement(MainNs + "sheets", new XElement(MainNs + "sheet", new XAttribute("name", sheetName), new XAttribute("sheetId", 1), new XAttribute(RelNs + "id", "rId1")))));

    private static XDocument WorkbookRelationships(AppData data)
    {
        var relationships = Enumerable.Range(1, 4)
            .Select(i => new XElement("Relationship",
                new XAttribute("Id", $"rId{i}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{i}.xml")))
            .Append(new XElement("Relationship",
                new XAttribute("Id", "rId5"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml")));
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(new XElement(ns + "Relationships", relationships.Select(item => new XElement(ns + "Relationship", item.Attributes()))));
    }

    private static XDocument SingleSheetWorkbookRelationships()
    {
        var relationships = new[]
        {
            new XElement("Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")),
            new XElement("Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml"))
        };
        XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
        return new XDocument(new XElement(ns + "Relationships", relationships.Select(item => new XElement(ns + "Relationship", item.Attributes()))));
    }
    private static XDocument Styles() => new(new XElement(MainNs + "styleSheet", new XElement(MainNs + "fonts", new XElement(MainNs + "font", new XElement(MainNs + "sz", new XAttribute("val", "11")), new XElement(MainNs + "name", new XAttribute("val", "Aptos")))), new XElement(MainNs + "fills", new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "none")))), new XElement(MainNs + "borders", new XElement(MainNs + "border")), new XElement(MainNs + "cellXfs", new XElement(MainNs + "xf"))));
    private static XDocument Sheet(IReadOnlyList<string> headers, IEnumerable<string[]> values) => new(new XElement(MainNs + "worksheet", new XElement(MainNs + "sheetData", Row(headers, 1), values.Select((row, index) => Row(row, index + 2)))));
    private static XElement Row(IEnumerable<string> values, int index) => new(MainNs + "row", new XAttribute("r", index), values.Select((value, cellIndex) => new XElement(MainNs + "c", new XAttribute("r", CellReference(cellIndex, index)), new XAttribute("t", "inlineStr"), new XElement(MainNs + "is", new XElement(MainNs + "t", value ?? "")))));

    public byte[] CreateFormattedSampleWorkbook(string sheetName, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            WriteXml(archive, "[Content_Types].xml", SingleSheetContentTypes());
            WriteXml(archive, "_rels/.rels", Relationships());
            WriteXml(archive, "xl/workbook.xml", SingleSheetWorkbook(sheetName));
            WriteXml(archive, "xl/_rels/workbook.xml.rels", SingleSheetWorkbookRelationships());
            WriteXml(archive, "xl/styles.xml", FormattedSampleStyles());
            WriteXml(archive, "xl/worksheets/sheet1.xml", FormattedSampleSheet(headers, rows));
        }
        return output.ToArray();
    }

    private static XDocument FormattedSampleStyles()
    {
        return new XDocument(new XElement(MainNs + "styleSheet",
            new XElement(MainNs + "fonts", new XAttribute("count", 2),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "sz", new XAttribute("val", "11")),
                    new XElement(MainNs + "name", new XAttribute("val", "Calibri"))
                ),
                new XElement(MainNs + "font",
                    new XElement(MainNs + "b"),
                    new XElement(MainNs + "sz", new XAttribute("val", "11")),
                    new XElement(MainNs + "name", new XAttribute("val", "Calibri"))
                )
            ),
            new XElement(MainNs + "fills", new XAttribute("count", 2),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "gray125")))
            ),
            new XElement(MainNs + "borders", new XAttribute("count", 1),
                new XElement(MainNs + "border",
                    new XElement(MainNs + "left"),
                    new XElement(MainNs + "right"),
                    new XElement(MainNs + "top"),
                    new XElement(MainNs + "bottom"),
                    new XElement(MainNs + "diagonal")
                )
            ),
            new XElement(MainNs + "cellStyleXfs", new XAttribute("count", 1),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))
            ),
            new XElement(MainNs + "cellXfs", new XAttribute("count", 2),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0)),
                new XElement(MainNs + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 1), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyFont", 1))
            ),
            new XElement(MainNs + "cellStyles", new XAttribute("count", 1),
                new XElement(MainNs + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))
            )
        ));
    }

    private static XDocument FormattedSampleSheet(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var rowList = rows.ToList();

        var colsElement = new XElement(MainNs + "cols");
        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            double colWidth = Math.Max(16.0, header.Length + 4.0);
            var lower = header.ToLowerInvariant();
            if (lower.Contains("iccid")) colWidth = Math.Max(colWidth, 24.0);
            else if (lower.Contains("imei")) colWidth = Math.Max(colWidth, 20.0);
            else if (lower.Contains("airtel") || lower.Contains("bsnl") || lower.Contains("sim") || lower.Contains("cell")) colWidth = Math.Max(colWidth, 20.0);
            else if (lower.Contains("vehicle")) colWidth = Math.Max(colWidth, 18.0);
            else if (lower.Contains("notes")) colWidth = Math.Max(colWidth, 30.0);
            else if (lower.Contains("date")) colWidth = Math.Max(colWidth, 18.0);

            colsElement.Add(new XElement(MainNs + "col",
                new XAttribute("min", i + 1),
                new XAttribute("max", i + 1),
                new XAttribute("width", colWidth.ToString("F1", CultureInfo.InvariantCulture)),
                new XAttribute("customWidth", "1")
            ));
        }

        var sheetData = new XElement(MainNs + "sheetData");

        var headerRow = new XElement(MainNs + "row",
            new XAttribute("r", 1),
            headers.Select((h, cellIndex) =>
                new XElement(MainNs + "c",
                    new XAttribute("r", CellReference(cellIndex, 1)),
                    new XAttribute("s", 1),
                    new XAttribute("t", "inlineStr"),
                    new XElement(MainNs + "is", new XElement(MainNs + "t", h))
                )
            )
        );
        sheetData.Add(headerRow);

        for (int r = 0; r < rowList.Count; r++)
        {
            var rowIndex = r + 2;
            var values = rowList[r];
            var dataRow = new XElement(MainNs + "row",
                new XAttribute("r", rowIndex),
                headers.Select((_, cellIndex) =>
                {
                    var val = cellIndex < values.Length ? values[cellIndex] ?? "" : "";
                    return new XElement(MainNs + "c",
                        new XAttribute("r", CellReference(cellIndex, rowIndex)),
                        new XAttribute("t", "inlineStr"),
                        new XElement(MainNs + "is", new XElement(MainNs + "t", val))
                    );
                })
            );
            sheetData.Add(dataRow);
        }

        return new XDocument(new XElement(MainNs + "worksheet",
            new XElement(MainNs + "sheetFormatPr", new XAttribute("defaultRowHeight", "18")),
            colsElement,
            sheetData
        ));
    }

    private static string CellReference(int column, int row)
    {
        var name = "";
        var value = column + 1;
        while (value > 0) { var remainder = (value - 1) % 26; name = (char)('A' + remainder) + name; value = (value - 1) / 26; }
        return name + row;
    }

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static void WriteXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Utf8NoBom);
        document.Save(writer, SaveOptions.None);
    }

    public List<ParsedInvoiceDeviceRow> ParseImportedInvoice(Stream stream, string fileName)
    {
        List<Dictionary<string, string>> rows;
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            rows = ReadExcelRows(stream);
        }
        else
        {
            rows = ReadCsvRows(stream);
        }

        var result = new List<ParsedInvoiceDeviceRow>();
        foreach (var row in rows)
        {
            var iccidVal = GetFlexibleValue(row, "iccid", "iccic", "icci") ?? GetValue(row, "iccid");
            var imeiVal = GetFlexibleValue(row, "imei") ?? GetValue(row, "imei");
            var airtelVal = GetFlexibleValue(row, "airtel", "sim_airtel") ?? GetValue(row, "airtel");
            var bsnlVal = GetFlexibleValue(row, "bsnl", "sim_bsnl") ?? GetValue(row, "bsnl");
            var serialVal = GetFlexibleValue(row, "serial_number", "serialnumber", "serial number", "device_serial_no", "deviceserialno", "device serial no", "serial_no", "serial no", "device_serial", "device serial", "serial", "s.no", "sl_no", "sr_no") ?? GetValue(row, "serial");

            if (!string.IsNullOrWhiteSpace(iccidVal) &&
                !string.IsNullOrWhiteSpace(imeiVal))
            {
                result.Add(new ParsedInvoiceDeviceRow
                {
                    Iccid = iccidVal.Trim(),
                    Imei = imeiVal.Trim(),
                    Airtel = airtelVal?.Trim() ?? "",
                    Bsnl = bsnlVal?.Trim() ?? "",
                    DeviceSerialNo = serialVal?.Trim() ?? ""
                });
            }
        }
        return result;
    }

    public static List<Dictionary<string, string>> ReadExcelRows(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));
        if (sheetEntry is null) return [];
        return ReadRows(archive, sheetEntry.FullName, sharedStrings).ToList();
    }

    public List<DeviceRecord> ParseImportedDevices(Stream stream, string fileName)
    {
        List<Dictionary<string, string>> rows;
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            rows = ReadExcelRows(stream);
        }
        else
        {
            rows = ReadCsvRows(stream);
        }

        var result = new List<DeviceRecord>();
        int slNoCounter = 0;
        foreach (var row in rows)
        {
            var iccid = GetFlexibleValue(row, "iccid", "iccic", "icci");
            var imei = GetFlexibleValue(row, "imei");

            // If both ICCID and IMEI are empty, skip row
            if (string.IsNullOrWhiteSpace(iccid) && string.IsNullOrWhiteSpace(imei))
            {
                continue;
            }

            slNoCounter++;
            var record = new DeviceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SlNo = GetFlexibleValue(row, "sl_no", "slno", "s.no", "sr no") ?? slNoCounter.ToString(),
                VehicleNo = GetFlexibleValue(row, "vehicle_no", "vehicleno", "vehicle") ?? "",
                Iccid = iccid?.Trim() ?? "",
                Imei = imei?.Trim() ?? "",
                DeviceSerialNo = GetFlexibleValue(row, "serial_number", "serialnumber", "serial number", "device_serial_no", "deviceserialno", "device serial no", "serial_no", "serial no", "device_serial", "device serial", "serial") ?? "",
                SimAirtel = GetFlexibleValue(row, "sim_airtel", "airtel", "sim airtel", "sim1") ?? "",
                SimBsnl = GetFlexibleValue(row, "sim_bsnl", "bsnl", "sim bsnl", "sim2") ?? "",
                Status = GetFlexibleValue(row, "status") ?? "Active",
                Type = NormalizeType(GetFlexibleValue(row, "type", "device_type") ?? "NEW"),
                DeviceName = NormalizeDeviceName(GetFlexibleValue(row, "device_name", "devicename", "device model", "model")),
                InvoiceId = GetFlexibleValue(row, "invoice_id", "invoiceno", "invoice_no") ?? "",
                Vendor = GetFlexibleValue(row, "vendor") ?? "",
                Owner = GetFlexibleValue(row, "owner") ?? "",
                CellNo = GetFlexibleValue(row, "cell_no", "cellno", "contact", "mobile", "phone") ?? "",
                RegistrationDate = GetFlexibleDate(row, "registration_date", "reg_date"),
                ActivationDate = GetFlexibleDate(row, "activation_date", "act_date"),
                LatestExpiryDate = GetFlexibleDate(row, "latest_expiry_date", "expiry_date", "exp_date"),
                EngineNo = GetFlexibleValue(row, "engine_no", "engineno", "engine") ?? "",
                ChasisNo = GetFlexibleValue(row, "chasis_no", "chassis_no", "chasisno", "chassis") ?? "",
                VehicleMake = GetFlexibleValue(row, "vehicle_make", "vehicle_manufacture", "vehicle manufacture", "vehicle_manufacturer", "make", "manufacturer") ?? "",
                VehicleModel = GetFlexibleValue(row, "vehicle_model", "vehicle model", "model") ?? "",
                Rto = NormalizeRto(GetFlexibleValue(row, "rto", "rto_office", "rto_no", "rto no"), GetFlexibleValue(row, "vehicle_no", "vehicleno", "vehicle")),
                Address = GetFlexibleValue(row, "address", "addr", "glimpse_address") ?? "",
                ProofOfAddress = GetFlexibleValue(row, "proof_of_address", "proof_of_identity", "proof of identity", "poa", "poi") ?? "",
                DocumentNumber = GetFlexibleValue(row, "document_number", "id_number", "id number", "doc_no", "docnumber", "document number") ?? "",
                Notes = GetFlexibleValue(row, "notes", "remarks") ?? "",
                LifecycleStatus = GetFlexibleValue(row, "lifecycle_status") ?? "Active",
                StockStatus = GetFlexibleValue(row, "stock_status") ?? "Used",
                CdacCertificate = GetFlexibleValue(row, "cdac_certificate", "cdac_certifcte", "cdac_c", "cdac", "cdacc")?.ToUpper() ?? "NO",
                VldtCertificate = GetFlexibleValue(row, "vldt_certificate", "vldt_c", "vltd-c", "vldtc", "vltd_c", "vltd")?.ToUpper() ?? "NO",
                CreatedAt = DateTime.UtcNow.ToString("O")
            };
            result.Add(record);
        }
        return result;
    }

    public List<TransactionRecord> ParseImportedTransactions(Stream stream, string fileName)
    {
        List<Dictionary<string, string>> rows;
        if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            rows = ReadExcelRows(stream);
        }
        else
        {
            rows = ReadCsvRows(stream);
        }

        var result = new List<TransactionRecord>();
        int slNoCounter = 0;
        foreach (var row in rows)
        {
            var vehicleNo = GetFlexibleValue(row, "vehicle_no", "vehicleno", "vehicle") ?? "";
            var date = GetFlexibleDate(row, "date", "tx_date", "payment_date", "txn_date");
            var amount = GetFlexibleDecimal(row, "amount", "total_amount", "paid_amount", "amt");

            // If vehicle and date and amount are all blank/zero, skip row
            if (string.IsNullOrWhiteSpace(vehicleNo) && string.IsNullOrWhiteSpace(date) && amount == 0m)
            {
                continue;
            }

            slNoCounter++;
            var record = new TransactionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SlNo = GetFlexibleValue(row, "sl_no", "slno", "s.no", "sr no", "serial") ?? slNoCounter.ToString(),
                Date = !string.IsNullOrWhiteSpace(date) ? date : DateTime.UtcNow.ToString("yyyy-MM-dd"),
                VehicleNo = vehicleNo.Trim(),
                Vendor = GetFlexibleValue(row, "vendor", "vendor_name") ?? "",
                PaymentMode = GetFlexibleValue(row, "payment_mode", "mode", "pay_mode") ?? "UPI",
                PaymentFor = GetFlexibleValue(row, "payment_for", "for", "type", "payment_type") ?? "Recharge",
                Amount = amount,
                BatchId = GetFlexibleValue(row, "batch_id", "batch", "batchno", "batch_no") ?? "",
                Utr = GetFlexibleValue(row, "utr", "utr_no", "transaction_id", "utr/transaction_id", "tx_id") ?? "",
                PaidTo = GetFlexibleValue(row, "paid_to", "paidto") ?? "",
                Extras = NormalizeExtras(GetFlexibleValue(row, "extras", "extra")),
                ActivationDate = GetFlexibleDate(row, "activation_date", "act_date"),
                NewExpiryDate = GetFlexibleDate(row, "new_expiry_date", "expiry_date", "exp_date"),
                Profit = GetFlexibleDecimal(row, "profit", "profits", "margin"),
                Notes = GetFlexibleValue(row, "notes", "remarks", "note") ?? "",
                CreatedAt = DateTime.UtcNow.ToString("O")
            };
            result.Add(record);
        }
        return result;
    }

    private static decimal GetFlexibleDecimal(Dictionary<string, string> row, params string[] patterns)
    {
        var val = GetFlexibleValue(row, patterns);
        if (string.IsNullOrWhiteSpace(val)) return 0m;
        val = val.Replace("₹", "").Replace(",", "").Trim();
        return decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static string? GetFlexibleValue(Dictionary<string, string> row, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = row.FirstOrDefault(item =>
            {
                var key = item.Key.Replace("_", "").Replace(" ", "").Trim();
                var pat = pattern.Replace("_", "").Replace(" ", "").Trim();
                return string.Equals(key, pat, StringComparison.OrdinalIgnoreCase);
            });
            if (match.Key is not null && !string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value.Trim();
            }
        }
        foreach (var pattern in patterns)
        {
            var match = row.FirstOrDefault(item => item.Key.Contains(pattern, StringComparison.OrdinalIgnoreCase));
            if (match.Key is not null && !string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Value.Trim();
            }
        }
        return null;
    }

    private static int GetFlexibleInt(Dictionary<string, string> row, int fallback, params string[] patterns)
    {
        var val = GetFlexibleValue(row, patterns);
        return int.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static string GetFlexibleDate(Dictionary<string, string> row, params string[] patterns)
    {
        var val = GetFlexibleValue(row, patterns);
        if (string.IsNullOrWhiteSpace(val)) return "";
        if (double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var dSerial) && dSerial > 30000 && dSerial < 65000)
        {
            try { return DateTime.FromOADate(dSerial).ToString("yyyy-MM-dd"); } catch { }
        }
        var dt = DateValue.Parse(val);
        return dt.HasValue ? dt.Value.ToString("yyyy-MM-dd") : val;
    }

    private static List<Dictionary<string, string>> ReadCsvRows(Stream stream)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
        var rows = new List<Dictionary<string, string>>();
        var line = reader.ReadLine();
        if (line is null) return rows;
        var headers = ParseCsvLine(line);
        var uniqueHeaders = new List<string>();
        var headerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
        {
            var headerName = string.IsNullOrWhiteSpace(h) ? "Column" : h.Trim();
            if (headerCounts.TryGetValue(headerName, out int count))
            {
                count++;
                headerCounts[headerName] = count;
                uniqueHeaders.Add($"{headerName}_{count}");
            }
            else
            {
                headerCounts[headerName] = 1;
                uniqueHeaders.Add(headerName);
            }
        }

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = ParseCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < uniqueHeaders.Count; i++)
            {
                var val = i < values.Count ? values[i] : "";
                row[uniqueHeaders[i]] = val;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim('"', ' ', '\t'));
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim('"', ' ', '\t'));
        return result;
    }

    private static string? GetValue(Dictionary<string, string> row, string pattern)
    {
        var match = row.FirstOrDefault(item => item.Key.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        return match.Value;
    }

    public byte[] CreateMultiSheetWorkbook(IReadOnlyList<MultiSheetItem> sheets)
    {
        if (sheets == null || sheets.Count == 0)
        {
            return CreateFormattedSampleWorkbook("Sheet1", ["No Data"], []);
        }

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            // [Content_Types].xml
            XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            var contentTypes = new XDocument(new XElement(ctNs + "Types",
                new XElement(ctNs + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ctNs + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ctNs + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                sheets.Select((_, idx) => new XElement(ctNs + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{idx + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))
            ));
            WriteXml(archive, "[Content_Types].xml", contentTypes);

            // _rels/.rels
            WriteXml(archive, "_rels/.rels", Relationships());

            // xl/workbook.xml
            var workbookDoc = new XDocument(new XElement(MainNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", RelNs),
                new XElement(MainNs + "sheets",
                    sheets.Select((s, idx) => new XElement(MainNs + "sheet",
                        new XAttribute("name", s.SheetName),
                        new XAttribute("sheetId", idx + 1),
                        new XAttribute(RelNs + "id", $"rId{idx + 1}")
                    ))
                )
            ));
            WriteXml(archive, "xl/workbook.xml", workbookDoc);

            // xl/_rels/workbook.xml.rels
            XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var sheetRels = sheets.Select((_, idx) => new XElement(relNs + "Relationship",
                new XAttribute("Id", $"rId{idx + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{idx + 1}.xml")
            )).Append(new XElement(relNs + "Relationship",
                new XAttribute("Id", $"rId{sheets.Count + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                new XAttribute("Target", "styles.xml")
            ));
            WriteXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(new XElement(relNs + "Relationships", sheetRels)));

            // xl/styles.xml
            WriteXml(archive, "xl/styles.xml", FormattedSampleStyles());

            // xl/worksheets/sheet{i}.xml
            for (int i = 0; i < sheets.Count; i++)
            {
                WriteXml(archive, $"xl/worksheets/sheet{i + 1}.xml", FormattedSampleSheet(sheets[i].Headers, sheets[i].Rows));
            }
        }
        return output.ToArray();
    }

    public static string SanitizeSheetName(string? rawName, HashSet<string> usedNames)
    {
        var clean = string.IsNullOrWhiteSpace(rawName) ? "Batch" : rawName.Trim();
        clean = new string(clean.Select(c => "\\/?*:[ ]".Contains(c) ? '-' : c).ToArray());
        if (string.IsNullOrWhiteSpace(clean)) clean = "Batch";
        if (clean.Length > 31) clean = clean.Substring(0, 31);

        var baseName = clean;
        var counter = 1;
        while (usedNames.Contains(clean))
        {
            var suffix = $"_{counter++}";
            var maxBaseLen = Math.Max(1, 31 - suffix.Length);
            var prefix = baseName.Length > maxBaseLen ? baseName.Substring(0, maxBaseLen) : baseName;
            clean = prefix + suffix;
        }
        usedNames.Add(clean);
        return clean;
    }

    public static Dictionary<string, string> ReadSheetMap(ZipArchive archive)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wbEntry = archive.GetEntry("xl/workbook.xml");
        if (wbEntry is null) return result;

        var relsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry != null)
        {
            using var relStream = relsEntry.Open();
            var relDoc = XDocument.Load(relStream);
            XNamespace relNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            foreach (var rel in relDoc.Descendants(relNs + "Relationship"))
            {
                var id = (string?)rel.Attribute("Id");
                var target = (string?)rel.Attribute("Target");
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                {
                    var normalized = target.TrimStart('/');
                    if (!normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = "xl/" + normalized;
                    }
                    relsMap[id] = normalized;
                }
            }
        }

        using (var stream = wbEntry.Open())
        {
            var wbDoc = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            foreach (var sheet in wbDoc.Descendants(ns + "sheet"))
            {
                var name = (string?)sheet.Attribute("name");
                var rId = (string?)sheet.Attribute(rNs + "id") ?? (string?)sheet.Attribute("id");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(rId) && relsMap.TryGetValue(rId, out var path))
                {
                    result[name] = path;
                }
            }
        }
        return result;
    }

    public ParsedInvoiceWorkbookResult ParseInvoicesWorkbook(Stream stream, string fileName)
    {
        var result = new ParsedInvoiceWorkbookResult();
        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            result.FallbackRosterDevices = ParseImportedInvoice(stream, fileName);
            return result;
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetMap = ReadSheetMap(archive);

        // Find main Invoices sheet:
        // Match sheets explicitly named "Invoices", "Invoices_DMG", "Invoices_RTO" or containing "invoices"
        var invoicesSheetEntry = sheetMap.FirstOrDefault(kv =>
            string.Equals(kv.Key, "Invoices", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kv.Key, "Invoices_DMG", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kv.Key, "Invoices_RTO", StringComparison.OrdinalIgnoreCase));

        if (invoicesSheetEntry.Key is null)
        {
            invoicesSheetEntry = sheetMap.FirstOrDefault(kv => 
                kv.Key.IndexOf("invoices", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !kv.Key.StartsWith("INV-", StringComparison.OrdinalIgnoreCase));
        }

        if (invoicesSheetEntry.Key is null)
        {
            // If no sheet has "invoices" in name, check if the first sheet has invoice-like headers
            var firstSheet = sheetMap.FirstOrDefault();
            if (firstSheet.Key != null)
            {
                var testRows = ReadRows(archive, firstSheet.Value, sharedStrings).ToList();
                if (testRows.Count > 0 && (testRows[0].ContainsKey("invoice_no") || testRows[0].ContainsKey("invoiceno") || testRows[0].ContainsKey("rate")))
                {
                    invoicesSheetEntry = firstSheet;
                }
                else
                {
                    result.FallbackRosterDevices = ParseDeviceRosterRows(testRows);
                    return result;
                }
            }
            else
            {
                return result;
            }
        }

        result.HasInvoicesSheet = true;
        var invoiceRows = ReadRows(archive, invoicesSheetEntry.Value, sharedStrings).ToList();

        foreach (var row in invoiceRows)
        {
            var invoiceNo = GetFlexibleValue(row, "invoice_no", "invoiceno", "invoice no", "invoice", "batch_id", "batch");
            if (string.IsNullOrWhiteSpace(invoiceNo)) continue;

            var invDate = GetFlexibleDate(row, "date", "invoice_date", "inv_date");
            var devType = GetFlexibleValue(row, "device_type", "devicetype", "type", "model") ?? "GPS";
            var qty = GetFlexibleInt(row, 0, "quantity", "qty", "units");
            var rate = GetFlexibleDecimal(row, "rate", "device_cost", "unit_cost", "price");
            var courier = GetFlexibleDecimal(row, "courier_charges", "extra_charges", "courier", "extras");
            var pQty = GetFlexibleInt(row, qty, "panic_button_quantity", "panic_qty", "pb_qty");
            var pCost = GetFlexibleDecimal(row, "panic_button_cost", "panic_cost", "pb_cost");
            var notes = GetFlexibleValue(row, "notes", "remarks") ?? "";

            var netOperator = GetFlexibleValue(row, "network_operator", "networkoperator", "operator") ?? "";

            var invoice = new InvoiceRecord
            {
                InvoiceNo = invoiceNo.Trim(),
                Date = !string.IsNullOrWhiteSpace(invDate) ? invDate : DateTime.UtcNow.ToString("yyyy-MM-dd"),
                DeviceType = devType,
                NetworkOperator = netOperator,
                Quantity = qty,
                Rate = rate,
                CourierCharges = courier,
                PanicButtonQuantity = pQty > 0 ? pQty : qty,
                PanicButtonCost = pCost,
                Notes = notes,
                CreatedAt = DateTime.UtcNow.ToString("O")
            };

            var package = new ParsedInvoicePackage
            {
                Invoice = invoice,
                SheetName = invoiceNo.Trim()
            };

            // Look for matching sheet for this invoice in the same workbook
            var matchingSheet = sheetMap.FirstOrDefault(kv =>
                !string.Equals(kv.Key, invoicesSheetEntry.Key, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(kv.Key.Trim(), invoiceNo.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(SanitizeSheetName(kv.Key, new HashSet<string>()), SanitizeSheetName(invoiceNo, new HashSet<string>()), StringComparison.OrdinalIgnoreCase)));

            if (matchingSheet.Key != null)
            {
                var deviceRows = ReadRows(archive, matchingSheet.Value, sharedStrings).ToList();
                package.Devices = ParseDeviceRosterRows(deviceRows);
                package.FullDevices = ParseFullDeviceRows(deviceRows, invoiceNo.Trim());
            }

            result.Packages.Add(package);
        }

        return result;
    }

    public static List<ParsedInvoiceDeviceRow> ParseDeviceRosterRows(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var result = new List<ParsedInvoiceDeviceRow>();
        foreach (var row in rows)
        {
            var iccidVal = GetFlexibleValue(row, "iccid", "iccic", "icci") ?? GetValue(row, "iccid");
            var imeiVal = GetFlexibleValue(row, "imei") ?? GetValue(row, "imei");
            var serialVal = GetFlexibleValue(row, "serial_number", "serialnumber", "serial number", "device_serial_no", "deviceserialno", "device serial no", "serial_no", "serial no", "device_serial", "device serial", "serial", "s.no", "sl_no", "sr_no") ?? GetValue(row, "serial");
            var airtelVal = GetFlexibleValue(row, "airtel", "sim_airtel") ?? GetValue(row, "airtel");
            var bsnlVal = GetFlexibleValue(row, "bsnl", "sim_bsnl") ?? GetValue(row, "bsnl");

            if (!string.IsNullOrWhiteSpace(iccidVal) || !string.IsNullOrWhiteSpace(imeiVal))
            {
                result.Add(new ParsedInvoiceDeviceRow
                {
                    Iccid = iccidVal?.Trim() ?? "",
                    Imei = imeiVal?.Trim() ?? "",
                    DeviceSerialNo = serialVal?.Trim() ?? "",
                    Airtel = airtelVal?.Trim() ?? "",
                    Bsnl = bsnlVal?.Trim() ?? ""
                });
            }
        }
        return result;
    }

    public static List<DeviceRecord> ParseFullDeviceRows(IReadOnlyList<Dictionary<string, string>> rows, string invoiceNo)
    {
        var result = new List<DeviceRecord>();
        foreach (var row in rows)
        {
            var imei = GetFlexibleValue(row, "imei");
            var iccid = GetFlexibleValue(row, "iccid", "iccic", "icci");
            if (string.IsNullOrWhiteSpace(imei) && string.IsNullOrWhiteSpace(iccid)) continue;

            var d = new DeviceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                InvoiceId = invoiceNo,
                Iccid = iccid?.Trim() ?? "",
                Imei = imei?.Trim() ?? "",
                DeviceSerialNo = GetFlexibleValue(row, "serial_number", "serialnumber", "serial number", "device_serial_no", "deviceserialno", "device serial no", "serial_no", "serial no", "device_serial", "device serial", "serial") ?? "",
                VehicleNo = GetFlexibleValue(row, "vehicle_no", "vehicleno", "vehicle") ?? "",
                SimAirtel = GetFlexibleValue(row, "sim_airtel", "airtel", "sim airtel", "sim1") ?? "",
                SimBsnl = GetFlexibleValue(row, "sim_bsnl", "bsnl", "sim bsnl", "sim2") ?? "",
                Status = GetFlexibleValue(row, "status") ?? "Active",
                Type = GetFlexibleValue(row, "type", "device_type") ?? "GPS",
                DeviceName = NormalizeDeviceName(GetFlexibleValue(row, "device_name", "devicename", "device model", "model")),
                Vendor = GetFlexibleValue(row, "vendor") ?? "",
                Owner = GetFlexibleValue(row, "owner") ?? "",
                CellNo = GetFlexibleValue(row, "cell_no", "cellno", "contact", "mobile") ?? "",
                RegistrationDate = GetFlexibleDate(row, "registration_date", "reg_date"),
                ActivationDate = GetFlexibleDate(row, "activation_date", "act_date"),
                LatestExpiryDate = GetFlexibleDate(row, "latest_expiry_date", "expiry_date", "exp_date"),
                Notes = GetFlexibleValue(row, "notes", "remarks") ?? "",
                LifecycleStatus = GetFlexibleValue(row, "lifecycle_status") ?? "Active",
                StockStatus = GetFlexibleValue(row, "stock_status") ?? "New",
                CreatedAt = DateTime.UtcNow.ToString("O")
            };
            result.Add(d);
        }
        return result;
    }
}

public sealed class MultiSheetItem
{
    public string SheetName { get; set; } = "";
    public IReadOnlyList<string> Headers { get; set; } = [];
    public IEnumerable<string[]> Rows { get; set; } = [];
}

public sealed class ParsedInvoicePackage
{
    public InvoiceRecord Invoice { get; set; } = new();
    public List<ParsedInvoiceDeviceRow> Devices { get; set; } = [];
    public List<DeviceRecord> FullDevices { get; set; } = [];
    public string SheetName { get; set; } = "";
}

public sealed class ParsedInvoiceWorkbookResult
{
    public bool HasInvoicesSheet { get; set; }
    public List<ParsedInvoicePackage> Packages { get; set; } = [];
    public List<ParsedInvoiceDeviceRow> FallbackRosterDevices { get; set; } = [];
}

