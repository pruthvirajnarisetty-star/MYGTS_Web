using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MygtsMvc.Models;

namespace MygtsMvc.Services;

public enum PermissionLevel
{
    View,
    Edit
}

public static class PermissionExtensions
{
    public static bool IsAdmin(this ClaimsPrincipal principal)
        => principal.IsInRole("Admin");

    public static bool IsExecutive(this ClaimsPrincipal principal)
        => principal.IsInRole("Admin") || principal.IsInRole("Executive");

    public static bool IsViewer(this ClaimsPrincipal principal)
        => principal.IsInRole("User");

    public static bool HasBranchAccess(this ClaimsPrincipal principal, string branch)
    {
        if (principal.IsAdmin()) return true;
        var allowed = principal.FindFirst("AllowedBranches")?.Value;
        if (string.IsNullOrWhiteSpace(allowed)) return true;
        var list = allowed.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return list.Any(b => string.Equals(b, branch, StringComparison.OrdinalIgnoreCase));
    }

    public static List<string> GetAllowedBranches(this ClaimsPrincipal principal)
    {
        if (principal.IsAdmin()) return new List<string> { "DMG", "RTO", "Expenditure" };
        var allowed = principal.FindFirst("AllowedBranches")?.Value;
        if (string.IsNullOrWhiteSpace(allowed)) return new List<string> { "DMG", "RTO", "Expenditure" };
        return allowed.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public static string GetModulePermission(this ClaimsPrincipal principal, string module)
    {
        if (principal.IsExecutive()) return "Edit";

        var permissionsClaim = principal.FindFirst("Permissions")?.Value;
        if (string.IsNullOrWhiteSpace(permissionsClaim)) return "Read";

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(permissionsClaim);
            if (dict != null && dict.TryGetValue(module, out var perm) && !string.IsNullOrWhiteSpace(perm))
            {
                if (principal.IsInRole("User") && perm == "Edit") return "Read";
                return perm;
            }
        }
        catch { }

        return "Read";
    }

    public static bool CanViewModule(this ClaimsPrincipal principal, string module)
    {
        if (principal.IsExecutive()) return true;
        if (string.Equals(module, "Inventory", StringComparison.OrdinalIgnoreCase) && principal.CanEditModule("Devices"))
            return true;
        return principal.GetModulePermission(module) != "None";
    }

    public static bool CanEditModule(this ClaimsPrincipal principal, string module)
    {
        if (principal.IsExecutive()) return true;
        if (principal.IsInRole("User")) return false;
        return principal.GetModulePermission(module) == "Edit";
    }

    public static bool CanEditInventoryActions(this ClaimsPrincipal principal)
    {
        if (principal.IsExecutive()) return true;
        if (principal.IsInRole("User")) return false;
        return principal.CanEditModule("Inventory") || principal.CanEditModule("Devices");
    }

    public static bool CanViewReminder(this ClaimsPrincipal principal, ManualReminder reminder)
    {
        if (principal.IsExecutive()) return true;
        return principal.IsAddressedInReminder(reminder);
    }

    public static bool IsAddressedInReminder(this ClaimsPrincipal principal, ManualReminder reminder)
    {
        if (reminder == null) return false;

        // Admins and Executives have global oversight
        if (principal.IsExecutive()) return true;

        var audience = (reminder.TargetAudience ?? "").Trim();
        if (string.IsNullOrWhiteSpace(audience)) return false;

        var username = principal.Identity?.Name ?? "";
        var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? "";
        var userRole = principal.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var emailPrefix = email.Contains('@') ? email.Split('@')[0] : email;

        // Check creator
        var isCreator = !string.IsNullOrWhiteSpace(reminder.CreatedBy) && 
            (reminder.CreatedBy.Equals(username, StringComparison.OrdinalIgnoreCase) || 
             reminder.CreatedBy.Equals(email, StringComparison.OrdinalIgnoreCase) ||
             reminder.CreatedBy.Equals(emailPrefix, StringComparison.OrdinalIgnoreCase));

        if (isCreator)
        {
            return true;
        }

        // Check if addressed to all/everyone
        if (audience.Equals("@all", StringComparison.OrdinalIgnoreCase) || 
            audience.Equals("@everyone", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check mentions/tags: e.g. @user1, @team1, @Team, @User, etc.
        var tokens = audience.Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var clean = token.Trim().TrimStart('@');
            if (clean.Equals(username, StringComparison.OrdinalIgnoreCase) || 
                clean.Equals(email, StringComparison.OrdinalIgnoreCase) ||
                clean.Equals(emailPrefix, StringComparison.OrdinalIgnoreCase) ||
                clean.Equals(userRole, StringComparison.OrdinalIgnoreCase) ||
                ((clean.Equals("me", StringComparison.OrdinalIgnoreCase) || clean.Equals("self", StringComparison.OrdinalIgnoreCase)) && isCreator))
            {
                return true;
            }
        }

        return false;
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireModulePermissionAttribute : Attribute, IAsyncActionFilter
{
    public string Module { get; }
    public PermissionLevel Level { get; }
    public string? AlternativeModule { get; set; }
    public PermissionLevel AlternativeLevel { get; set; } = PermissionLevel.Edit;

    public RequireModulePermissionAttribute(string module, PermissionLevel level = PermissionLevel.Edit)
    {
        Module = module;
        Level = level;
    }

    /* [Title: Legacy Async Action Filter with State Machine Struct]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        bool isAnonymous = false;
        try
        {
            if (context.ActionDescriptor.EndpointMetadata?.Any(em => em is Microsoft.AspNetCore.Authorization.IAllowAnonymous) == true)
            {
                isAnonymous = true;
            }
            else if (context.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor cad)
            {
                isAnonymous = cad.MethodInfo.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true);
            }
        }
        catch
        {
            // Transient reflection fallback during hot reload
        }

        if (isAnonymous)
        {
            await next();
            return;
        }

        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        bool allowed = Level switch
        {
            PermissionLevel.View => user.CanViewModule(Module),
            PermissionLevel.Edit => user.CanEditModule(Module),
            _ => false
        };

        if (!allowed)
        {
            if (context.HttpContext.Request.Headers.XRequestedWith == "XMLHttpRequest" || context.HttpContext.Request.ContentType?.Contains("json") == true)
            {
                context.Result = new JsonResult(new { success = false, message = "Access denied." }) { StatusCode = 403 };
            }
            else
            {
                context.Result = new RedirectToActionResult("Denied", "Account", new { area = "" });
            }
            return;
        }

        await next();
    }
    */

    // [Title: Direct Task Action Filter - Avoids Transient State Machine Struct Collisions]
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        bool isAnonymous = false;
        try
        {
            if (context.ActionDescriptor.EndpointMetadata?.Any(em => em is Microsoft.AspNetCore.Authorization.IAllowAnonymous) == true)
            {
                isAnonymous = true;
            }
            else if (context.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor cad)
            {
                isAnonymous = cad.MethodInfo.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true);
            }
        }
        catch
        {
            // Transient reflection fallback during hot reload
        }

        if (isAnonymous)
        {
            return next();
        }

        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return Task.CompletedTask;
        }

        bool allowed = Level switch
        {
            PermissionLevel.View => user.CanViewModule(Module),
            PermissionLevel.Edit => user.CanEditModule(Module),
            _ => false
        };

        if (!allowed && !string.IsNullOrWhiteSpace(AlternativeModule))
        {
            allowed = AlternativeLevel switch
            {
                PermissionLevel.View => user.CanViewModule(AlternativeModule),
                PermissionLevel.Edit => user.CanEditModule(AlternativeModule),
                _ => false
            };
        }

        if (!allowed)
        {
            if (context.HttpContext.Request.Headers.XRequestedWith == "XMLHttpRequest" || context.HttpContext.Request.ContentType?.Contains("json") == true)
            {
                context.Result = new JsonResult(new { success = false, message = "Access denied." }) { StatusCode = 403 };
            }
            else
            {
                context.Result = new RedirectToActionResult("Denied", "Account", new { area = "" });
            }
            return Task.CompletedTask;
        }

        return next();
    }
}

public static class UserRoleHierarchy
{
    public const string Admin = "Admin";
    public const string Executive = "Executive";
    public const string Team = "Team";
    public const string User = "User";

    public static int GetRank(string? role) => (role?.Trim().ToLowerInvariant()) switch
    {
        "admin" => 4,
        "executive" => 3,
        "team" => 2,
        "user" => 1,
        _ => 0
    };

    public static int GetRank(this ClaimsPrincipal principal)
    {
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        return GetRank(role);
    }

    /// <summary>
    /// Determines whether current user can view a target user in lists, dropdowns, and directory views.
    /// Professional hierarchy rule:
    /// - Only Executives and Admins can see other users below their roles.
    /// - Admin can see users strictly below Admin (Executive, Team, User) and self.
    /// - Executive can see users strictly below Executive (Team, User) and self.
    /// - Team and User roles can ONLY see their own profile and nothing else.
    /// - No user can see peers (same rank) or superiors (higher rank).
    /// </summary>
    public static bool CanViewUser(this ClaimsPrincipal principal, UserRecord? target)
    {
        if (target == null) return false;

        var myId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(myId) && target.Id == myId) return true;

        var myEmail = principal.FindFirst(ClaimTypes.Email)?.Value;
        if (!string.IsNullOrEmpty(myEmail) && string.Equals(target.Email?.Trim(), myEmail.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

        var myName = principal.Identity?.Name;
        if (!string.IsNullOrEmpty(myName) && 
            (string.Equals(target.DisplayName?.Trim(), myName.Trim(), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(target.Email?.Trim(), myName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var myRank = principal.GetRank();
        var targetRank = GetRank(target.Role);

        // ONLY Admin (rank 4) and Executive (rank 3) can view OTHER users strictly below their roles
        if (myRank >= 3)
        {
            return targetRank < myRank;
        }

        // Team (rank 2) and User (rank 1) roles can ONLY see their own profile and nothing else
        return false;
    }

    /// <summary>
    /// Determines whether current user can manage (modify, toggle, delete) a target user.
    /// - Only Admin and Executive can manage accounts strictly below them (targetRank < currentRank).
    /// - Team and User cannot manage any other user.
    /// </summary>
    public static bool CanManageUser(this ClaimsPrincipal principal, UserRecord? target)
    {
        if (target == null) return false;

        var myRank = principal.GetRank();
        var targetRank = GetRank(target.Role);

        // ONLY Admin (rank 4) and Executive (rank 3) can manage accounts strictly below them
        if (myRank >= 3)
        {
            return targetRank < myRank;
        }

        return false;
    }

    /// <summary>
    /// Returns the subordinate roles that the current user is permitted to create or assign.
    /// - Admin: Executive, Team, User (subordinate roles below Admin)
    /// - Executive: Team, User (subordinate roles below Executive)
    /// - Team & User: none (cannot create or assign roles)
    /// </summary>
    public static List<string> GetAllowedRolesToAssign(this ClaimsPrincipal principal)
    {
        var myRank = principal.GetRank();
        var roles = new List<string>();
        if (myRank >= 4) // Admin
        {
            roles.Add("Executive");
            roles.Add("Team");
            roles.Add("User");
        }
        else if (myRank == 3) // Executive
        {
            roles.Add("Team");
            roles.Add("User");
        }
        // Team and User roles cannot assign or create accounts
        return roles;
    }
}
