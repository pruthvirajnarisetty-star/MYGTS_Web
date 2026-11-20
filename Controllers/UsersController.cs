using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MygtsMvc.Models;
using MygtsMvc.Services;
using MygtsMvc.ViewModels;

namespace MygtsMvc.Controllers;

[Authorize]
public sealed class UsersController : Controller
{
    private readonly IAppDataStore _store;
    private readonly PasswordService _passwords;
    private readonly IExpenditureDataStore _expenditureStore;

    public UsersController(IAppDataStore store, PasswordService passwords, IExpenditureDataStore expenditureStore)
    {
        _store = store;
        _passwords = passwords;
        _expenditureStore = expenditureStore;
    }

    private bool IsSelf(UserRecord? user)
    {
        if (user == null) return false;
        var myId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(myId) && user.Id == myId) return true;
        var myEmail = User.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(myEmail) && string.Equals(user.Email, myEmail, StringComparison.OrdinalIgnoreCase)) return true;
        var myName = User.Identity?.Name;
        if (!string.IsNullOrEmpty(myName) && (string.Equals(user.DisplayName, myName, StringComparison.OrdinalIgnoreCase) || string.Equals(user.Email, myName, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    [HttpGet]
    [Route("Profile")]
    [Route("Users/Profile")]
    public async Task<IActionResult> Profile()
    {
        var snapshot = await _store.SnapshotAsync();
        var user = snapshot.Users.FirstOrDefault(u => IsSelf(u));
        if (user == null)
        {
            TempData["Error"] = "Profile not found.";
            return RedirectToAction(nameof(Index));
        }
        return RedirectToAction(nameof(Edit), new { id = user.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var snapshot = await _store.SnapshotAsync();
        var visibleUsers = snapshot.Users
            .Where(u => User.CanViewUser(u))
            .OrderBy(item => item.DisplayName)
            .ToList();

        var model = new UserListViewModel
        {
            Users = visibleUsers,
            IsAdmin = User.IsAdmin(),
            CurrentUserEmail = User.Identity?.Name ?? "",
            CurrentUserRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "User",
            AllowedRoles = User.GetAllowedRolesToAssign()
        };
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(UserFormViewModel model)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Index));
        
        var allowedRoles = User.GetAllowedRolesToAssign();
        if (allowedRoles.Count == 0)
        {
            TempData["Error"] = "You do not have permission to create user accounts.";
            return RedirectToAction(nameof(Index));
        }

        var exists = (await _store.SnapshotAsync()).Users.Any(item => string.Equals(item.Email, model.Email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exists) { TempData["Error"] = "A user with that email already exists."; return RedirectToAction(nameof(Index)); }
        
        var role = model.Role;
        if (!allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            role = allowedRoles.Last();
        }

        var perms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in AppModules.All)
        {
            if (role == "User")
            {
                bool isAllowed = model.Permissions.TryGetValue(m, out var p) && (p == "Read" || p == "Edit" || p == "true" || p == "on");
                perms[m] = isAllowed ? "Read" : "None";
            }
            else if (role == "Executive" || role == "Admin")
            {
                perms[m] = "Edit";
            }
            else
            {
                perms[m] = model.Permissions.TryGetValue(m, out var p) && !string.IsNullOrWhiteSpace(p) ? p : "Read";
            }
        }
        var permissionsJson = System.Text.Json.JsonSerializer.Serialize(perms);

        var allowedBranches = role == "Admin" 
            ? "DMG,RTO,Expenditure" 
            : (model.Branches != null && model.Branches.Count > 0 ? string.Join(",", model.Branches) : "DMG,RTO,Expenditure");

        await _store.UpdateAsync(data => data.Users.Add(new UserRecord 
        { 
            Email = model.Email.Trim().ToLowerInvariant(), 
            DisplayName = model.DisplayName.Trim(), 
            Role = role, 
            PasswordHash = _passwords.Hash(model.Password.Trim()),
            PlainPassword = model.Password.Trim(),
            PermissionsJson = permissionsJson,
            AllowedBranches = allowedBranches
        }));
        TempData["Success"] = $"User {model.DisplayName} created successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Toggle(string id)
    {
        var snapshot = await _store.SnapshotAsync();
        var targetUser = snapshot.Users.FirstOrDefault(item => item.Id == id);
        if (targetUser is null) return RedirectToAction(nameof(Index));

        if (IsSelf(targetUser))
        {
            TempData["Error"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Index));
        }

        if (!User.CanManageUser(targetUser))
        {
            TempData["Error"] = "You do not have permission to modify this user account.";
            return RedirectToAction(nameof(Index));
        }

        if (targetUser.Role == "Admin" && targetUser.IsActive)
        {
            var otherActiveAdmins = snapshot.Users.Count(u => u.Role == "Admin" && u.IsActive && u.Id != targetUser.Id);
            if (otherActiveAdmins == 0)
            {
                TempData["Error"] = "Cannot deactivate this account. At least one Admin must remain active as a safeguard.";
                return RedirectToAction(nameof(Index));
            }
        }

        bool newStatus = false;
        await _store.UpdateAsync(data =>
        {
            var user = data.Users.FirstOrDefault(item => item.Id == id);
            if (user is not null)
            {
                user.IsActive = !user.IsActive;
                newStatus = user.IsActive;
            }
        });

        TempData["Success"] = $"User {targetUser.DisplayName} is now {(newStatus ? "Active" : "Disabled")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var snapshot = await _store.SnapshotAsync();
        var user = snapshot.Users.FirstOrDefault(item => item.Id == id);
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        bool isSelf = IsSelf(user);
        if (!isSelf && !User.CanManageUser(user))
        {
            TempData["Error"] = "You do not have permission to edit this user account.";
            return RedirectToAction(nameof(Index));
        }

        var perms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in AppModules.All)
        {
            perms[m] = user.GetPermission(m);
        }

        var model = new UserEditFormViewModel
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive,
            PlainPassword = user.PlainPassword,
            IsProtectedAdmin = (user.Role == "Admin" && !User.IsAdmin()),
            WhatsAppPreference = string.IsNullOrWhiteSpace(user.WhatsAppPreference) ? "Ask" : user.WhatsAppPreference,
            Branches = user.GetAllowedBranchesList(),
            Permissions = perms,
            AllowedRoles = User.GetAllowedRolesToAssign()
        };

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(UserEditFormViewModel model)
    {
        var snapshot = await _store.SnapshotAsync();

        var userToUpdate = snapshot.Users.FirstOrDefault(item => item.Id == model.Id);
        if (userToUpdate is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        bool isCurrentUser = IsSelf(userToUpdate);
        if (!isCurrentUser && !User.CanManageUser(userToUpdate))
        {
            TempData["Error"] = "You do not have permission to modify this user account.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            model.AllowedRoles = User.GetAllowedRolesToAssign();
            return View(model);
        }

        var exists = snapshot.Users.Any(item =>
            item.Id != model.Id &&
            string.Equals(item.Email, model.Email.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            ModelState.AddModelError(nameof(model.Email), "A user with that email already exists.");
            model.AllowedRoles = User.GetAllowedRolesToAssign();
            return View(model);
        }

        if (isCurrentUser)
        {
            if (!model.IsActive)
            {
                ModelState.AddModelError(nameof(model.IsActive), "You cannot deactivate your own account.");
                model.AllowedRoles = User.GetAllowedRolesToAssign();
                return View(model);
            }
            if (userToUpdate.Role == "Admin" && model.Role != "Admin")
            {
                ModelState.AddModelError(nameof(model.Role), "You cannot demote your own Admin role.");
                model.AllowedRoles = User.GetAllowedRolesToAssign();
                return View(model);
            }
        }

        if (userToUpdate.Role == "Admin")
        {
            var otherActiveAdmins = snapshot.Users.Count(u => u.Role == "Admin" && u.IsActive && u.Id != userToUpdate.Id);
            if (!model.IsActive && otherActiveAdmins == 0)
            {
                ModelState.AddModelError(nameof(model.IsActive), "Cannot deactivate this account. At least one Admin must remain active as a safeguard.");
                model.AllowedRoles = User.GetAllowedRolesToAssign();
                return View(model);
            }
            if (model.Role != "Admin" && otherActiveAdmins == 0)
            {
                ModelState.AddModelError(nameof(model.Role), "Cannot demote this account. At least one Admin must remain in the system.");
                model.AllowedRoles = User.GetAllowedRolesToAssign();
                return View(model);
            }
        }

        var allowedRoles = User.GetAllowedRolesToAssign();
        var role = model.Role;
        if (!isCurrentUser)
        {
            if (!allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                role = userToUpdate.Role;
            }
        }
        else
        {
            // Self-edit retains existing role
            role = userToUpdate.Role;
        }

        var perms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in AppModules.All)
        {
            if (role == "User")
            {
                bool isAllowed = model.Permissions.TryGetValue(m, out var p) && (p == "Read" || p == "Edit" || p == "true" || p == "on");
                perms[m] = isAllowed ? "Read" : "None";
            }
            else if (role == "Executive" || role == "Admin")
            {
                perms[m] = "Edit";
            }
            else
            {
                perms[m] = model.Permissions.TryGetValue(m, out var p) && !string.IsNullOrWhiteSpace(p) ? p : "Read";
            }
        }
        var permissionsJson = System.Text.Json.JsonSerializer.Serialize(perms);

        var allowedBranches = role == "Admin" 
            ? "DMG,RTO,Expenditure" 
            : (model.Branches != null && model.Branches.Count > 0 ? string.Join(",", model.Branches) : "DMG,RTO,Expenditure");

        await _store.UpdateAsync(data =>
        {
            var user = data.Users.FirstOrDefault(item => item.Id == model.Id);
            if (user is not null)
            {
                user.DisplayName = model.DisplayName.Trim();
                user.Email = model.Email.Trim().ToLowerInvariant();

                if (!isCurrentUser)
                {
                    user.Role = role;
                    user.IsActive = model.IsActive;
                    user.PermissionsJson = permissionsJson;
                    user.AllowedBranches = allowedBranches;
                }

                user.WhatsAppPreference = string.IsNullOrWhiteSpace(model.WhatsAppPreference) ? "Ask" : model.WhatsAppPreference;

                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    user.PasswordHash = _passwords.Hash(model.Password.Trim());
                    user.PlainPassword = model.Password.Trim();
                }
            }
        });

        // Live sync updated credentials to Expenditure store if present
        try
        {
            var expUser = await _expenditureStore.GetUserByEmailAsync(userToUpdate.Email);
            if (expUser != null)
            {
                expUser.DisplayName = model.DisplayName.Trim().ToUpperInvariant();
                expUser.Email = model.Email.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    expUser.PasswordHash = _passwords.Hash(model.Password.Trim());
                    expUser.PlainPassword = model.Password.Trim();
                }
                if (!isCurrentUser)
                {
                    expUser.Role = role.ToUpperInvariant();
                    expUser.IsActive = model.IsActive;
                }
                await _expenditureStore.UpdateUserAsync(expUser);
            }
        }
        catch { }

        TempData["Success"] = "User profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        var snapshot = await _store.SnapshotAsync();
        var user = snapshot.Users.FirstOrDefault(item => item.Id == id);
        if (user is null)
        {
            TempData["Error"] = "User not found.";
            return RedirectToAction(nameof(Index));
        }

        if (IsSelf(user))
        {
            TempData["Error"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(Index));
        }

        if (!User.CanManageUser(user))
        {
            TempData["Error"] = "You do not have permission to delete this user account.";
            return RedirectToAction(nameof(Index));
        }

        if (user.Role == "Admin")
        {
            var otherAdmins = snapshot.Users.Count(u => u.Role == "Admin" && u.Id != user.Id);
            if (otherAdmins == 0)
            {
                TempData["Error"] = "Cannot delete this Admin account. At least one Admin must remain in the system.";
                return RedirectToAction(nameof(Index));
            }
        }

        await _store.UpdateAsync(data =>
        {
            var userRecord = data.Users.FirstOrDefault(item => item.Id == id);
            if (userRecord is not null)
            {
                data.Users.Remove(userRecord);
            }
        });

        TempData["Success"] = "User deleted successfully.";
        return RedirectToAction(nameof(Index));
    }
}
