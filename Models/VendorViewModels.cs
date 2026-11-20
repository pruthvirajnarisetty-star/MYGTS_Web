using System.Collections.Generic;
using MygtsMvc.Models;

namespace MygtsMvc.Controllers;

public class VendorProfileViewModel
{
    public VendorRecord Vendor { get; set; } = new();
    public int TotalDevices { get; set; }
    public decimal TotalAmountPaid { get; set; }
    public decimal AverageAmountPaid { get; set; }
    public int UnpaidDevicesCount { get; set; }
    public Dictionary<string, int> DeviceTypes { get; set; } = new();
    public List<DeviceRecord> ExpiringDevices { get; set; } = new();
}

public class UpdateVendorProfileRequest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string BatchPrefix { get; set; } = "";
    public string Phone { get; set; } = "";
    public string AltPhone { get; set; } = "";
    public string ExtraPhones { get; set; } = "";
    public string WhatsAppNumber { get; set; } = "";
    public bool WhatsAppSameAsPhone { get; set; } = true;
    public string Notes { get; set; } = "";
}
