using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using System.Security.Claims;

namespace MygtsMvc.Areas.Expenditure.Controllers;

[Area("Expenditure")]
[Authorize]
public class UsersController : Controller
{
    private readonly IExpenditureDataStore _store;
    private readonly PasswordService _passwords;

    public UsersController(IExpenditureDataStore store, PasswordService passwords)
    {
        _store = store;
        _passwords = passwords;
    }

    private bool CheckAdminAccess()
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(role, "ADMIN", StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet]
    public IActionResult Index()
    {
        return Redirect("/Users");
    }

    [HttpGet]
    public IActionResult Create()
    {
        if (!CheckAdminAccess()) return Forbid();

        return View(new ExpenditureUserRecord());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ExpenditureUserRecord model)
    {
        if (!CheckAdminAccess()) return Forbid();

        if (string.IsNullOrWhiteSpace(model.Email))
            ModelState.AddModelError("Email", "EMAIL / USERNAME IS REQUIRED.");
        if (string.IsNullOrWhiteSpace(model.DisplayName))
            ModelState.AddModelError("DisplayName", "DISPLAY NAME IS REQUIRED.");
        if (string.IsNullOrWhiteSpace(model.PlainPassword))
            ModelState.AddModelError("PlainPassword", "PASSWORD IS REQUIRED.");

        var existing = await _store.GetUserByEmailAsync(model.Email);
        if (existing != null)
        {
            ModelState.AddModelError("Email", "A USER WITH THIS EMAIL ALREADY EXISTS.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.PasswordHash = _passwords.Hash(model.PlainPassword);
        model.CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await _store.CreateUserAsync(model);
        TempData["SuccessMessage"] = "USER CREATED SUCCESSFULLY.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (!CheckAdminAccess()) return Forbid();

        var users = await _store.GetAllUsersAsync();
        var user = users.FirstOrDefault(u => u.Id == id);
        if (user == null) return NotFound();

        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ExpenditureUserRecord model)
    {
        if (!CheckAdminAccess()) return Forbid();

        if (string.IsNullOrWhiteSpace(model.DisplayName))
            ModelState.AddModelError("DisplayName", "DISPLAY NAME IS REQUIRED.");
        if (string.IsNullOrWhiteSpace(model.PlainPassword))
            ModelState.AddModelError("PlainPassword", "PASSWORD IS REQUIRED.");

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.PasswordHash = _passwords.Hash(model.PlainPassword);
        await _store.UpdateUserAsync(model);
        TempData["SuccessMessage"] = "USER UPDATED SUCCESSFULLY.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        if (!CheckAdminAccess()) return Forbid();

        await _store.DeleteUserAsync(id);
        TempData["SuccessMessage"] = "USER REMOVED SUCCESSFULLY.";
        return RedirectToAction(nameof(Index));
    }
}

