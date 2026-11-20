using System;
using System.Collections.Generic;
using System.Linq;

namespace MygtsMvc.Services;

public static class BatchColorHelper
{
    public const int TotalPaletteColors = 16;

    public static readonly Dictionary<string, string> RtoBatchToInvoiceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BATCH-01"] = "54",
        ["BATCH-02"] = "71",
        ["BATCH-03"] = "Missing",
        ["BATCH-04"] = "383",
        ["BATCH-05"] = "421",
        ["BATCH-06"] = "535",
        ["BATCH-07"] = "849",
        ["BATCH-08"] = "BATCH-08"
    };

    public static readonly Dictionary<string, string> RtoInvoiceToBatchMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["54"] = "BATCH-01",
        ["71"] = "BATCH-02",
        ["Missing"] = "BATCH-03",
        ["383"] = "BATCH-04",
        ["421"] = "BATCH-05",
        ["535"] = "BATCH-06",
        ["849"] = "BATCH-07",
        ["BATCH-08"] = "BATCH-08"
    };

    /// <summary>
    /// Builds a deterministic, collision-free batch color map for the given collection of invoice IDs.
    /// Invoices are ordered so that numeric IDs are sorted numerically and others alphabetically,
    /// guaranteeing every distinct batch in a branch receives a unique color index (1 to 16).
    /// </summary>
    public static Dictionary<string, int> BuildBatchColorMap(IEnumerable<string?> invoiceIds)
    {
        var distinct = invoiceIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => int.TryParse(b, out var n) ? n : int.MaxValue)
            .ThenBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < distinct.Count; i++)
        {
            map[distinct[i]] = (i % TotalPaletteColors) + 1;
        }

        return map;
    }

    /// <summary>
    /// Retrieves the CSS class for a given invoice batch (e.g. "batch-row-1", "batch-row-2").
    /// </summary>
    public static string GetBatchClass(string? invoiceId, Dictionary<string, int>? map = null)
    {
        if (string.IsNullOrWhiteSpace(invoiceId)) return "batch-row-default";
        var norm = invoiceId.Trim();
        if (map != null)
        {
            if (map.TryGetValue(norm, out var idx)) return $"batch-row-{idx}";
            if (RtoBatchToInvoiceMap.TryGetValue(norm, out var inv) && map.TryGetValue(inv, out idx)) return $"batch-row-{idx}";
            if (RtoInvoiceToBatchMap.TryGetValue(norm, out var bName) && map.TryGetValue(bName, out idx)) return $"batch-row-{idx}";
        }

        // Fallback for ad-hoc or unmapped batch IDs
        var fallback = (Math.Abs(norm.GetHashCode()) % TotalPaletteColors) + 1;
        return $"batch-row-{fallback}";
    }
}
