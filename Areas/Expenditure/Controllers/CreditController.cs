using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using System.Security.Claims;

namespace MygtsMvc.Areas.Expenditure.Controllers;

[Area("Expenditure")]
[Authorize]
public class CreditController : Controller
{
    private readonly IExpenditureDataStore _store;

    public CreditController(IExpenditureDataStore store)
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
    public async Task<IActionResult> Index(string? q = null, string? status = null, string? dir = null, string? fromDate = null, string? toDate = null)
    {
        if (!CheckAccess()) return Forbid();

        Response.Cookies.Append("mygts_branch", "Expenditure", new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            HttpOnly = false
        });

        var allCredits = await _store.GetAllCreditsAsync();
        var filtered = allCredits.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var cleanQ = q.Trim().ToUpperInvariant();
            filtered = filtered.Where(c => 
                c.CompanyName.ToUpperInvariant().Contains(cleanQ) ||
                (c.Description != null && c.Description.ToUpperInvariant().Contains(cleanQ)) ||
                (c.Notes != null && c.Notes.ToUpperInvariant().Contains(cleanQ)) ||
                c.CreditAmount.ToString().Contains(cleanQ) ||
                c.Date.Contains(cleanQ)
            );
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(status, "PAID_OFF", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(c => c.IsPaidOff);
            else if (string.Equals(status, "OUTSTANDING", StringComparison.OrdinalIgnoreCase))
                filtered = filtered.Where(c => !c.IsPaidOff);
        }

        if (!string.IsNullOrWhiteSpace(dir) && !string.Equals(dir, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(c => string.Equals(c.CreditType, dir, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            var f = fromDate.Trim();
            filtered = filtered.Where(c => !string.IsNullOrEmpty(c.Date) && string.Compare(c.Date, f, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(toDate))
        {
            var t = toDate.Trim();
            filtered = filtered.Where(c => !string.IsNullOrEmpty(c.Date) && string.Compare(c.Date, t, StringComparison.OrdinalIgnoreCase) <= 0);
        }

        var filteredList = filtered.ToList();
        var givenToUsList = allCredits.Where(c => c.IsGivenToUs).ToList();
        var givenByUsList = allCredits.Where(c => !c.IsGivenToUs).ToList();

        var vm = new ExpenditureCreditsViewModel
        {
            Credits = filteredList,
            SearchQuery = q,
            StatusFilter = status ?? "ALL",
            DirectionFilter = dir ?? "ALL",
            FromDate = fromDate,
            ToDate = toDate,

            TotalCreditAmount = allCredits.Sum(c => c.CreditAmount),
            TotalCompaniesCount = allCredits.Select(c => c.CompanyName.Trim().ToUpperInvariant()).Distinct().Count(),
            TotalCreditLines = allCredits.Count,

            // Given To Us
            TotalGivenToUs = givenToUsList.Sum(c => c.CreditAmount),
            OutstandingGivenToUs = givenToUsList.Where(c => !c.IsPaidOff).Sum(c => c.CreditAmount),
            PaidOffGivenToUs = givenToUsList.Where(c => c.IsPaidOff).Sum(c => c.CreditAmount),
            CountGivenToUs = givenToUsList.Count,

            // Given By Us
            TotalGivenByUs = givenByUsList.Sum(c => c.CreditAmount),
            OutstandingGivenByUs = givenByUsList.Where(c => !c.IsPaidOff).Sum(c => c.CreditAmount),
            PaidOffGivenByUs = givenByUsList.Where(c => c.IsPaidOff).Sum(c => c.CreditAmount),
            CountGivenByUs = givenByUsList.Count,

            // Overall Settlement
            TotalPaidOffAmount = allCredits.Where(c => c.IsPaidOff).Sum(c => c.CreditAmount),
            TotalOutstandingAmount = allCredits.Where(c => !c.IsPaidOff).Sum(c => c.CreditAmount),
            PaidOffCount = allCredits.Count(c => c.IsPaidOff),
            OutstandingCount = allCredits.Count(c => !c.IsPaidOff),

            FormModel = new ExpenditureCreditRecord
            {
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                CreditType = "GIVEN_TO_US",
                IsPaidOff = false
            }
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ExpenditureCreditsViewModel model)
    {
        if (!CheckAccess()) return Forbid();

        var rec = model.FormModel;
        if (string.IsNullOrWhiteSpace(rec.CompanyName))
            ModelState.AddModelError("FormModel.CompanyName", "COMPANY / PARTY NAME IS REQUIRED.");
        if (rec.CreditAmount <= 0)
            ModelState.AddModelError("FormModel.CreditAmount", "CREDIT AMOUNT MUST BE GREATER THAN ZERO.");

        if (!ModelState.IsValid)
        {
            var allCredits = await _store.GetAllCreditsAsync();
            var givenToUsList = allCredits.Where(c => c.IsGivenToUs).ToList();
            var givenByUsList = allCredits.Where(c => !c.IsGivenToUs).ToList();

            model.Credits = allCredits;
            model.TotalCreditAmount = allCredits.Sum(c => c.CreditAmount);
            model.TotalCompaniesCount = allCredits.Select(c => c.CompanyName.Trim().ToUpperInvariant()).Distinct().Count();
            model.TotalCreditLines = allCredits.Count;
            model.TotalGivenToUs = givenToUsList.Sum(c => c.CreditAmount);
            model.OutstandingGivenToUs = givenToUsList.Where(c => !c.IsPaidOff).Sum(c => c.CreditAmount);
            model.PaidOffGivenToUs = givenToUsList.Where(c => c.IsPaidOff).Sum(c => c.CreditAmount);
            model.CountGivenToUs = givenToUsList.Count;
            model.TotalGivenByUs = givenByUsList.Sum(c => c.CreditAmount);
            model.OutstandingGivenByUs = givenByUsList.Where(c => !c.IsPaidOff).Sum(c => c.CreditAmount);
            model.PaidOffGivenByUs = givenByUsList.Where(c => c.IsPaidOff).Sum(c => c.CreditAmount);
            model.CountGivenByUs = givenByUsList.Count;
            model.TotalPaidOffAmount = allCredits.Where(c => c.IsPaidOff).Sum(c => c.CreditAmount);
            model.TotalOutstandingAmount = allCredits.Where(c => !c.IsPaidOff).Sum(c => c.CreditAmount);
            model.PaidOffCount = allCredits.Count(c => c.IsPaidOff);
            model.OutstandingCount = allCredits.Count(c => !c.IsPaidOff);
            return View("Index", model);
        }

        rec.CreatedBy = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        rec.CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        if (rec.IsPaidOff && string.IsNullOrWhiteSpace(rec.PaidOffDate))
        {
            rec.PaidOffDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        }

        await _store.CreateCreditAsync(rec);
        var dirText = rec.IsGivenToUs ? "FROM" : "TO";
        TempData["SuccessMessage"] = $"CREDIT OF ₹{rec.CreditAmount:N2} {dirText} {rec.CompanyName.ToUpperInvariant()} ADDED SUCCESSFULLY.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ExpenditureCreditRecord credit)
    {
        if (!CheckAccess()) return Forbid();

        if (string.IsNullOrWhiteSpace(credit.Id)) return BadRequest();
        if (string.IsNullOrWhiteSpace(credit.CompanyName))
            return Json(new { success = false, message = "COMPANY / PARTY NAME IS REQUIRED." });
        if (credit.CreditAmount <= 0)
            return Json(new { success = false, message = "CREDIT AMOUNT MUST BE GREATER THAN ZERO." });

        var existing = await _store.GetCreditByIdAsync(credit.Id);
        if (existing == null) return NotFound();

        existing.CompanyName = credit.CompanyName.Trim().ToUpperInvariant();
        existing.CreditAmount = credit.CreditAmount;
        existing.CreditType = string.Equals(credit.CreditType, "GIVEN_BY_US", StringComparison.OrdinalIgnoreCase) ? "GIVEN_BY_US" : "GIVEN_TO_US";
        existing.IsPaidOff = credit.IsPaidOff;
        if (credit.IsPaidOff && string.IsNullOrWhiteSpace(credit.PaidOffDate))
        {
            existing.PaidOffDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        }
        else if (!credit.IsPaidOff)
        {
            existing.PaidOffDate = null;
        }
        else
        {
            existing.PaidOffDate = credit.PaidOffDate?.Trim();
        }

        if (!string.IsNullOrWhiteSpace(credit.Date)) existing.Date = credit.Date.Trim();
        existing.Description = credit.Description?.Trim().ToUpperInvariant();
        existing.Notes = credit.Notes?.Trim().ToUpperInvariant();

        await _store.UpdateCreditAsync(existing);
        TempData["SuccessMessage"] = $"CREDIT ENTRY FOR {existing.CompanyName} UPDATED SUCCESSFULLY.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(string id, string? returnUrl = null)
    {
        if (!CheckAccess()) return Forbid();

        var existing = await _store.GetCreditByIdAsync(id);
        if (existing == null) return NotFound();

        await _store.ToggleCreditPaidOffAsync(id);
        var newStatusText = !existing.IsPaidOff ? "MARKED AS PAID OFF / SETTLED" : "MARKED AS OUTSTANDING / ACTIVE";
        TempData["SuccessMessage"] = $"CREDIT ENTRY FOR {existing.CompanyName} {newStatusText}.";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (!CheckAccess()) return Forbid();

        var existing = await _store.GetCreditByIdAsync(id);
        if (existing == null) return NotFound();

        await _store.DeleteCreditAsync(id);
        TempData["SuccessMessage"] = $"CREDIT ENTRY FOR {existing.CompanyName} DELETED.";
        return RedirectToAction(nameof(Index));
    }
}

