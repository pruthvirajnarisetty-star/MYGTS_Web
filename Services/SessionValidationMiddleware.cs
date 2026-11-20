using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MygtsMvc.Services;

namespace MygtsMvc.Middleware;

public sealed class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;

    public SessionValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAppDataStore store)
    {
        // Skip if not authenticated
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Skip for static files and health checks
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var cookieToken = context.User.FindFirstValue("SessionToken");
        var userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? "";

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(cookieToken))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/Account/Login");
            return;
        }

        // Unified user validation from main store
        var user = await store.GetUserByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/Account/Login?reason=kicked");
            return;
        }
        else if (!string.IsNullOrEmpty(user.ActiveSessionToken) && user.ActiveSessionToken != cookieToken)
        {
            // Token mismatch — another login occurred; kick this session
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/Account/Login?reason=kicked");
            return;
        }

        // Branch authorization enforcement based on user's permitted branches
        if (path.StartsWith("/Expenditure", StringComparison.OrdinalIgnoreCase))
        {
            if (!user.HasBranchAccess("Expenditure"))
            {
                var allowed = user.GetAllowedBranchesList();
                if (allowed.Contains("DMG")) context.Response.Redirect("/DMG/Dashboard");
                else if (allowed.Contains("RTO")) context.Response.Redirect("/RTO/Dashboard");
                else context.Response.Redirect("/Account/Denied");
                return;
            }
        }
        else if (path.StartsWith("/DMG", StringComparison.OrdinalIgnoreCase))
        {
            if (!user.HasBranchAccess("DMG"))
            {
                var allowed = user.GetAllowedBranchesList();
                if (allowed.Contains("RTO")) context.Response.Redirect("/RTO/Dashboard");
                else if (allowed.Contains("Expenditure")) context.Response.Redirect("/Expenditure/Expenses");
                else context.Response.Redirect("/Account/Denied");
                return;
            }
        }
        else if (path.StartsWith("/RTO", StringComparison.OrdinalIgnoreCase))
        {
            if (!user.HasBranchAccess("RTO"))
            {
                var allowed = user.GetAllowedBranchesList();
                if (allowed.Contains("DMG")) context.Response.Redirect("/DMG/Dashboard");
                else if (allowed.Contains("Expenditure")) context.Response.Redirect("/Expenditure/Expenses");
                else context.Response.Redirect("/Account/Denied");
                return;
            }
        }

        await _next(context);
    }
}
