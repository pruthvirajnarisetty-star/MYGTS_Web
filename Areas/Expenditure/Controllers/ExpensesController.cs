using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using System.Security.Claims;

namespace MygtsMvc.Areas.Expenditure.Controllers;

[Area("Expenditure")]
[Authorize]
public class ExpensesController : Controller
{
    private readonly IExpenditureDataStore _store;

    public ExpensesController(IExpenditureDataStore store)
    {
        _store = store;
    }

    private bool CheckAccess()
    {
        if (User.IsAdmin()) return true;
        if (User.HasBranchAccess("Expenditure")) return true;
        return false;
    }

    [HttpGet]
    public async Task<IActionResult> Index(ExpenditureFilterViewModel filters)
    {
        if (!CheckAccess())
        {
            return Forbid();
        }

        // Set active branch cookie
        Response.Cookies.Append("mygts_branch", "Expenditure", new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            HttpOnly = false
        });

        var vm = await _store.GetExpendituresAsync(filters);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!CheckAccess()) return Forbid();

        var vm = new ExpenditureFormViewModel
        {
            Record = new ExpenditureRecord
            {
                RecordDate = DateTime.Now.ToString("yyyy-MM-dd"),
                PaymentDate = DateTime.Now.ToString("yyyy-MM-dd"),
                DocumentType = "PI",
                CashFlow = "DEBITED"
            },
            AvailableBanks = await _store.GetDropdownOptionsAsync("Bank"),
            AvailablePaymentTypes = await _store.GetDropdownOptionsAsync("PaymentType")
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ExpenditureFormViewModel model)
    {
        if (!CheckAccess()) return Forbid();

        model.AvailableBanks = await _store.GetDropdownOptionsAsync("Bank");
        model.AvailablePaymentTypes = await _store.GetDropdownOptionsAsync("PaymentType");

        var rec = model.Record;

        // Validation
        if (string.IsNullOrWhiteSpace(rec.ReferenceNumber))
            ModelState.AddModelError("Record.ReferenceNumber", "REFERENCE NUMBER IS REQUIRED.");
        if (string.IsNullOrWhiteSpace(rec.Company))
            ModelState.AddModelError("Record.Company", "COMPANY NAME IS REQUIRED.");
        if (string.IsNullOrWhiteSpace(rec.Purpose))
            ModelState.AddModelError("Record.Purpose", "FOR / PURPOSE IS REQUIRED.");

        if (rec.IsUnpaid)
        {
            ModelState.Remove("Record.Bank");
            ModelState.Remove("Record.PaymentType");
            if (string.IsNullOrWhiteSpace(rec.Bank)) rec.Bank = "UNPAID";
            if (string.IsNullOrWhiteSpace(rec.PaymentType)) rec.PaymentType = "UNPAID";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(rec.Bank) || rec.Bank.Equals("UNPAID", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError("Record.Bank", "BANK SELECTION IS REQUIRED.");
            if (string.IsNullOrWhiteSpace(rec.PaymentType) || rec.PaymentType.Equals("UNPAID", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError("Record.PaymentType", "PAYMENT TYPE IS REQUIRED.");
        }

        if (rec.Amount <= 0)
            ModelState.AddModelError("Record.Amount", "AMOUNT MUST BE GREATER THAN ZERO.");

        if (model.ConvertToInvoice)
        {
            if (string.IsNullOrWhiteSpace(rec.InvoiceNumber))
                ModelState.AddModelError("Record.InvoiceNumber", "INVOICE NUMBER IS REQUIRED WHEN CONVERTING.");
            if (string.IsNullOrWhiteSpace(rec.InvoiceDate))
                ModelState.AddModelError("Record.InvoiceDate", "INVOICE DATE IS REQUIRED WHEN CONVERTING.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        rec.CreatedBy = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        rec.CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        if (model.ConvertToInvoice)
        {
            rec.IsConvertedToInvoice = true;
            rec.DocumentType = "INVOICE";
            rec.OriginalPiNumber = rec.ReferenceNumber;
            rec.OriginalPiDate = rec.RecordDate;
            if (!string.IsNullOrWhiteSpace(rec.InvoiceNumber))
            {
                rec.ReferenceNumber = rec.InvoiceNumber.Trim().ToUpperInvariant();
            }
            if (!string.IsNullOrWhiteSpace(rec.InvoiceDate))
            {
                rec.RecordDate = rec.InvoiceDate.Trim();
            }
        }

        await _store.CreateExpenditureAsync(rec);
        TempData["SuccessMessage"] = "RECORD ADDED SUCCESSFULLY.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!CheckAccess()) return Forbid();

        var record = await _store.GetExpenditureByIdAsync(id);
        if (record == null) return NotFound();

        var vm = new ExpenditureFormViewModel
        {
            Record = record,
            ConvertToInvoice = record.IsConvertedToInvoice,
            AvailableBanks = await _store.GetDropdownOptionsAsync("Bank"),
            AvailablePaymentTypes = await _store.GetDropdownOptionsAsync("PaymentType")
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string? id, ExpenditureFormViewModel model)
    {
        if (!CheckAccess()) return Forbid();

        var rec = model.Record;
        if (string.IsNullOrWhiteSpace(rec.Id) && !string.IsNullOrWhiteSpace(id))
        {
            rec.Id = id;
        }

        if (string.IsNullOrWhiteSpace(rec.ReferenceNumber))
            ModelState.AddModelError("Record.ReferenceNumber", "REFERENCE NUMBER IS REQUIRED.");
        if (string.IsNullOrWhiteSpace(rec.Company))
            ModelState.AddModelError("Record.Company", "COMPANY NAME IS REQUIRED.");
        if (string.IsNullOrWhiteSpace(rec.Purpose))
            ModelState.AddModelError("Record.Purpose", "FOR / PURPOSE IS REQUIRED.");

        if (rec.IsUnpaid)
        {
            ModelState.Remove("Record.Bank");
            ModelState.Remove("Record.PaymentType");
            if (string.IsNullOrWhiteSpace(rec.Bank)) rec.Bank = "UNPAID";
            if (string.IsNullOrWhiteSpace(rec.PaymentType)) rec.PaymentType = "UNPAID";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(rec.Bank) || rec.Bank.Equals("UNPAID", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError("Record.Bank", "BANK SELECTION IS REQUIRED.");
            if (string.IsNullOrWhiteSpace(rec.PaymentType) || rec.PaymentType.Equals("UNPAID", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError("Record.PaymentType", "PAYMENT TYPE IS REQUIRED.");
        }

        if (rec.Amount <= 0)
            ModelState.AddModelError("Record.Amount", "AMOUNT MUST BE GREATER THAN ZERO.");

        if (model.ConvertToInvoice)
        {
            if (string.IsNullOrWhiteSpace(rec.InvoiceNumber))
                ModelState.AddModelError("Record.InvoiceNumber", "INVOICE NUMBER IS REQUIRED WHEN CONVERTING.");
            if (string.IsNullOrWhiteSpace(rec.InvoiceDate))
                ModelState.AddModelError("Record.InvoiceDate", "INVOICE DATE IS REQUIRED WHEN CONVERTING.");
        }

        if (!ModelState.IsValid)
        {
            model.AvailableBanks = await _store.GetDropdownOptionsAsync("Bank");
            model.AvailablePaymentTypes = await _store.GetDropdownOptionsAsync("PaymentType");
            return View(model);
        }

        var existing = await _store.GetExpenditureByIdAsync(rec.Id);
        if (existing == null) return NotFound();

        if (model.ConvertToInvoice)
        {
            rec.IsConvertedToInvoice = true;
            rec.DocumentType = "INVOICE";
            
            // Preserve original PI details if not already stored
            rec.OriginalPiNumber = !string.IsNullOrWhiteSpace(existing.OriginalPiNumber) 
                ? existing.OriginalPiNumber 
                : (!string.IsNullOrWhiteSpace(existing.ReferenceNumber) ? existing.ReferenceNumber : rec.ReferenceNumber);
            rec.OriginalPiDate = !string.IsNullOrWhiteSpace(existing.OriginalPiDate) 
                ? existing.OriginalPiDate 
                : (!string.IsNullOrWhiteSpace(existing.RecordDate) ? existing.RecordDate : rec.RecordDate);

            // Update primary reference and date to Invoice values
            if (!string.IsNullOrWhiteSpace(rec.InvoiceNumber))
            {
                rec.ReferenceNumber = rec.InvoiceNumber.Trim().ToUpperInvariant();
            }
            if (!string.IsNullOrWhiteSpace(rec.InvoiceDate))
            {
                rec.RecordDate = rec.InvoiceDate.Trim();
            }
        }
        else
        {
            rec.IsConvertedToInvoice = false;
        }

        // Preserve audit created info
        rec.CreatedAt = !string.IsNullOrWhiteSpace(existing.CreatedAt) ? existing.CreatedAt : DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        rec.CreatedBy = !string.IsNullOrWhiteSpace(existing.CreatedBy) ? existing.CreatedBy : (User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name));

        await _store.UpdateExpenditureAsync(rec);
        TempData["SuccessMessage"] = "RECORD UPDATED SUCCESSFULLY.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (!CheckAccess()) return Forbid();

        await _store.DeleteExpenditureAsync(id);
        TempData["SuccessMessage"] = "RECORD DELETED SUCCESSFULLY.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Export(ExpenditureFilterViewModel filters)
    {
        if (!CheckAccess()) return Forbid();

        var csvBytes = await _store.ExportToCsvAsync(filters);
        var filename = $"EXPENDITURE_REPORT_{DateTime.Now:yyyyMMdd_HHmm}.csv";
        return File(csvBytes, "text/csv", filename);
    }

    public class AddOptionDto
    {
        public string Category { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    [HttpPost]
    public async Task<IActionResult> AddOption([FromBody] AddOptionDto dto)
    {
        if (!CheckAccess()) return Forbid();

        if (string.IsNullOrWhiteSpace(dto?.Category) || string.IsNullOrWhiteSpace(dto?.Value))
        {
            return Json(new { success = false, message = "CATEGORY AND VALUE ARE REQUIRED." });
        }

        var ok = await _store.AddDropdownOptionAsync(dto.Category, dto.Value);
        return Json(new { success = ok, value = dto.Value.Trim().ToUpperInvariant() });
    }
}

