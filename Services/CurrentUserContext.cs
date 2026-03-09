using Invoxa.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Services;

public static class CurrentUserContext
{
    public static bool IsAuthenticated(HttpContext? http)
    {
        if (http == null) return false;
        return http.Request.Cookies.TryGetValue("invoxa_auth", out var email) && !string.IsNullOrWhiteSpace(email);
    }

    public static string? GetEmail(HttpContext? http)
    {
        if (http == null) return null;
        return http.Request.Cookies.TryGetValue("invoxa_auth", out var email) && !string.IsNullOrWhiteSpace(email)
            ? email.Trim().ToLowerInvariant()
            : null;
    }

    public static string GetRole(HttpContext? http)
    {
        if (http == null) return string.Empty;
        return http.Request.Cookies.TryGetValue("invoxa_role", out var role) && !string.IsNullOrWhiteSpace(role)
            ? role.Trim()
            : string.Empty;
    }

    public static bool IsSuperAdmin(HttpContext? http)
        => string.Equals(GetRole(http), "SuperAdmin", StringComparison.OrdinalIgnoreCase);

    public static async Task<Guid> RequireCompanyIdAsync(HttpContext? http, AppDbContext db)
    {
        if (http != null && http.Request.Cookies.TryGetValue("invoxa_auth", out var email) && !string.IsNullOrWhiteSpace(email))
        {
            var normalized = email.Trim().ToLowerInvariant();
            var user = await db.UserAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Email.ToLower() == normalized && x.IsActive);
            if (user != null) return user.CompanyId;
        }

        var fallback = await db.Companies.AsNoTracking().OrderBy(c => c.CreatedAtUtc).Select(c => c.Id).FirstOrDefaultAsync();
        if (fallback == Guid.Empty)
            throw new InvalidOperationException("No company found. Please register a workspace first.");

        return fallback;
    }
}
