using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Controllers;

public sealed class AccountController : Controller
{
    private readonly IAppDataStore _store;
    private readonly PasswordService _passwords;
    private readonly IExpenditureDataStore _expenditureStore;

    public AccountController(IAppDataStore store, PasswordService passwords, IExpenditureDataStore expenditureStore)
    {
        _store = store;
        _passwords = passwords;
        _expenditureStore = expenditureStore;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // If already authenticated with a valid session, redirect directly to dashboard
        if (User.Identity?.IsAuthenticated == true)
        {
            var branch = Request.Cookies["mygts_branch"];
            if (string.Equals(branch, "Expenditure", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect("/Expenditure/Expenses");
            }
            var target = string.Equals(branch, "RTO", StringComparison.OrdinalIgnoreCase) ? "RTO" : "DMG";
            return Redirect($"/{target}/Dashboard");
        }

        var dbStatus = await _store.CheckConnectionAsync();
        var existingBranch = Request.Cookies["mygts_branch"];
        var initialBranch = string.Equals(existingBranch, "Expenditure", StringComparison.OrdinalIgnoreCase) 
            ? "Expenditure" 
            : (string.Equals(existingBranch, "RTO", StringComparison.OrdinalIgnoreCase) ? "RTO" : "DMG");
            
        var model = new LoginViewModel
        {
            Branch = initialBranch,
            ReturnUrl = null,
            IsDatabaseConnected = dbStatus.IsConnected,
            DatabaseStatusMessage = dbStatus.Message,
            DatabasePath = dbStatus.FilePath
        };
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        // Re-check DB status so it stays visible on response
        var dbStatus = await _store.CheckConnectionAsync();
        model.IsDatabaseConnected = dbStatus.IsConnected;
        model.DatabaseStatusMessage = dbStatus.Message;
        model.DatabasePath = dbStatus.FilePath;

        if (!ModelState.IsValid) return View(model);

        var requestedBranch = (model.Branch ?? "DMG").Trim();
        var email = (model.Email ?? string.Empty).Trim();

        // 1. Unified user lookup from main store (with fallback to Expenditure store if needed)
        var user = await FindMainUserAsync(email);
        if (user == null)
        {
            var expUser = await FindExpenditureUserAsync(email);
            if (expUser != null)
            {
                // Auto-sync into main store
                user = new UserRecord
                {
                    Id = expUser.Id,
                    Email = expUser.Email.Trim().ToLowerInvariant(),
                    DisplayName = expUser.DisplayName,
                    PlainPassword = expUser.PlainPassword,
                    PasswordHash = expUser.PasswordHash,
                    Role = string.Equals(expUser.Role, "ADMIN", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Executive",
                    AllowedBranches = "Expenditure",
                    IsActive = expUser.IsActive,
                    CreatedAt = expUser.CreatedAt
                };
                await _store.UpdateAsync(d => d.Users.Add(user));
            }
        }

        if (user == null)
        {
            ModelState.AddModelError(string.Empty, $"THE USERNAME '{email}' IS NOT REGISTERED IN THE DATABASE.");
            return View(model);
        }

        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, $"THE USER ACCOUNT '{email}' HAS BEEN DEACTIVATED.");
            return View(model);
        }

        // Verify password
        bool isPasswordValid = _passwords.Verify(model.Password, user.PasswordHash) ||
                               string.Equals(model.Password, user.PlainPassword, StringComparison.Ordinal);

        if (!isPasswordValid)
        {
            ModelState.AddModelError(string.Empty, "INCORRECT PASSWORD. PLEASE VERIFY AND TRY AGAIN.");
            return View(model);
        }

        // 2. Verify branch authorization
        if (!user.HasBranchAccess(requestedBranch))
        {
            ModelState.AddModelError(string.Empty, $"YOUR ACCOUNT '{user.Email.ToUpperInvariant()}' IS NOT AUTHORIZED TO ACCESS THE {requestedBranch.ToUpperInvariant()} BRANCH.");
            return View(model);
        }

        var sessionToken = Guid.NewGuid().ToString("N");
        try
        {
            await _store.UpdateUserSessionTokenAsync(user.Id, sessionToken);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"DATABASE WRITE FAILED DURING LOGIN ({ex.Message}).");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("BranchAccess", user.Role == "Admin" ? "All" : (user.AllowedBranches ?? "All")),
            new("AllowedBranches", user.Role == "Admin" ? "DMG,RTO,Expenditure" : (user.AllowedBranches ?? "DMG,RTO,Expenditure")),
            new("Permissions", user.PermissionsJson ?? "{}"),
            new("SessionToken", sessionToken)
        };

        if (string.Equals(requestedBranch, "Expenditure", StringComparison.OrdinalIgnoreCase))
        {
            await SignInAndRedirectAsync(claims, "Expenditure");
            return Redirect("/Expenditure/Expenses");
        }
        else
        {
            var targetBranch = string.Equals(requestedBranch, "RTO", StringComparison.OrdinalIgnoreCase) ? "RTO" : "DMG";
            await SignInAndRedirectAsync(claims, targetBranch);
            return Redirect($"/{targetBranch}/Dashboard");
        }
    }

    private async Task<UserRecord?> FindMainUserAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var clean = email.Trim().ToLowerInvariant();

        var user = await _store.GetUserByEmailAsync(clean);
        if (user != null) return user;

        var snap = await _store.SnapshotAsync();
        user = snap.Users.FirstOrDefault(u =>
            string.Equals(u.Email?.Trim(), clean, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.DisplayName?.Trim(), clean, StringComparison.OrdinalIgnoreCase));
        if (user != null) return user;

        // Strip .com if present (e.g. admin@mygts.com -> admin@mygts)
        if (clean.EndsWith(".com"))
        {
            var withoutCom = clean[..^4];
            user = snap.Users.FirstOrDefault(u => string.Equals(u.Email?.Trim(), withoutCom, StringComparison.OrdinalIgnoreCase));
            if (user != null) return user;
        }

        // Add @mygts if no @ present (e.g. admin -> admin@mygts)
        if (!clean.Contains('@'))
        {
            var withDomain = clean + "@mygts";
            user = snap.Users.FirstOrDefault(u => string.Equals(u.Email?.Trim(), withDomain, StringComparison.OrdinalIgnoreCase));
            if (user != null) return user;
        }

        return null;
    }

    private async Task<ExpenditureUserRecord?> FindExpenditureUserAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var clean = email.Trim().ToLowerInvariant();

        var user = await _expenditureStore.GetUserByEmailAsync(clean);
        if (user != null) return user;

        var allUsers = await _expenditureStore.GetAllUsersAsync();
        user = allUsers.FirstOrDefault(u =>
            string.Equals(u.Email?.Trim(), clean, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.DisplayName?.Trim(), clean, StringComparison.OrdinalIgnoreCase));
        if (user != null) return user;

        if (clean == "admin")
        {
            user = allUsers.FirstOrDefault(u =>
                string.Equals(u.Email?.Trim(), "admin@mygts", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Email?.Trim(), "admin@expenditure", StringComparison.OrdinalIgnoreCase));
            if (user != null) return user;
        }

        if (!clean.Contains('@'))
        {
            var withMygts = clean + "@mygts";
            user = allUsers.FirstOrDefault(u => string.Equals(u.Email?.Trim(), withMygts, StringComparison.OrdinalIgnoreCase));
            if (user != null) return user;

            var withExp = clean + "@expenditure";
            user = allUsers.FirstOrDefault(u => string.Equals(u.Email?.Trim(), withExp, StringComparison.OrdinalIgnoreCase));
            if (user != null) return user;
        }

        if (clean.EndsWith(".com"))
        {
            var withoutCom = clean[..^4];
            user = allUsers.FirstOrDefault(u => string.Equals(u.Email?.Trim(), withoutCom, StringComparison.OrdinalIgnoreCase));
            if (user != null) return user;
        }

        // If not found in Expenditure table, check if user is an Administrator in the main store
        try
        {
            var mainSnapshot = await _store.SnapshotAsync();
            var mainAdmin = mainSnapshot.Users.FirstOrDefault(u =>
                (string.Equals(u.Email?.Trim(), clean, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(u.DisplayName?.Trim(), clean, StringComparison.OrdinalIgnoreCase) ||
                 (!clean.Contains('@') && string.Equals(u.Email?.Trim(), clean + "@mygts", StringComparison.OrdinalIgnoreCase))) &&
                string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase));

            if (mainAdmin != null)
            {
                var newExpAdmin = new ExpenditureUserRecord
                {
                    Id = mainAdmin.Id,
                    Email = mainAdmin.Email.Trim().ToUpperInvariant(),
                    DisplayName = (mainAdmin.DisplayName ?? mainAdmin.Email).Trim().ToUpperInvariant(),
                    PlainPassword = mainAdmin.PlainPassword ?? "",
                    PasswordHash = mainAdmin.PasswordHash ?? "",
                    Role = "ADMIN",
                    IsActive = mainAdmin.IsActive,
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                };
                await _expenditureStore.CreateUserAsync(newExpAdmin);
                return newExpAdmin;
            }
        }
        catch { }

        return null;
    }

    private async Task SignInAndRedirectAsync(List<Claim> claims, string branch)
    {
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProps = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProps);

        Response.Cookies.Append("mygts_branch", branch, new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(365),
            SameSite = SameSiteMode.Lax,
            HttpOnly = false
        });

        Response.Cookies.Append("mygts_just_logged_in", "1", new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromSeconds(30),
            Path = "/"
        });
    }

    [HttpGet, HttpPost]
    public async Task<IActionResult> Logout()
    {
        // Clear the active session token so the cookie can't be reused
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            await _store.UpdateUserSessionTokenAsync(userId, string.Empty);
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult Denied() => View();

    [HttpPost]
    public async Task<IActionResult> UpdateWhatsAppPreference([FromBody] WhatsAppPreferenceModel model)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized(new { success = false, message = "Not authenticated." });

        if (model == null || string.IsNullOrWhiteSpace(model.Preference))
            return BadRequest(new { success = false, message = "Invalid preference." });

        var pref = model.Preference.Trim();
        if (!string.Equals(pref, "Web", StringComparison.OrdinalIgnoreCase) && !string.Equals(pref, "App", StringComparison.OrdinalIgnoreCase))
        {
            pref = "Ask";
        }
        else
        {
            pref = string.Equals(pref, "Web", StringComparison.OrdinalIgnoreCase) ? "Web" : "App";
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

        await _store.UpdateAsync(data =>
        {
            var user = data.Users.FirstOrDefault(u => 
                (!string.IsNullOrEmpty(userId) && u.Id == userId) ||
                (!string.IsNullOrEmpty(email) && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(email) && string.Equals(u.DisplayName, email, StringComparison.OrdinalIgnoreCase)));

            if (user != null)
            {
                user.WhatsAppPreference = pref;
            }
        });

        return Json(new { success = true, preference = pref });
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View("Denied");
    }
}
