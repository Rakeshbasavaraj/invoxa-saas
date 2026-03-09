using System.ComponentModel.DataAnnotations;
using Invoxa.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Account;

public class LoginModel : PageModel
{
    private readonly AppDbContext _db;
    public LoginModel(AppDbContext db) => _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Message { get; set; }

    public IActionResult OnGet(string? message = null)
    {
        Message = message;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim().ToLowerInvariant();
        var user = await _db.UserAccounts.FirstOrDefaultAsync(x => x.Email.ToLower() == email && x.IsActive);
        if (user is null || !Services.PasswordHasher.Verify(Input.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return Page();
        }

        var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == user.CompanyId);
        if (company is null)
        {
            ModelState.AddModelError(string.Empty, "Company not found for this account.");
            return Page();
        }

        if (!string.Equals(user.Role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(company.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Your company is pending super admin approval. Please wait until it is approved.");
                return Page();
            }

            if (string.Equals(company.ApprovalStatus, "Suspended", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(string.Empty, "Your company is currently suspended. Please contact support.");
                return Page();
            }
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var expires = Input.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8);
        var cookieOptions = new CookieOptions
        {
            Expires = expires,
            IsEssential = true,
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Path = "/"
        };

        Response.Cookies.Append("invoxa_auth", user.Email, new CookieOptions
        {
            Expires = expires,
            IsEssential = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Path = "/"
        });
        Response.Cookies.Append("invoxa_user", user.FullName, cookieOptions);
        Response.Cookies.Append("invoxa_role", user.Role, cookieOptions);

        return RedirectToPage("/Index");
    }

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }
}
