using System.ComponentModel.DataAnnotations;
using Invoxa.Web.Data;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Settings;

public class UserModel : PageModel
{
    private readonly AppDbContext _db;

    public UserModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    [Required]
    public string UserName { get; set; } = "Admin";

    [BindProperty]
    public ChangePasswordInputModel PasswordInput { get; set; } = new();

    [BindProperty]
    public string ThemeName { get; set; } = "classic";

    public string? Message { get; set; }

    public void OnGet(string? message = null)
    {
        Message = message;
        if (Request.Cookies.TryGetValue("invoxa_user", out var v) && !string.IsNullOrWhiteSpace(v))
            UserName = v;
        if (Request.Cookies.TryGetValue("invoxa_theme", out var t) && !string.IsNullOrWhiteSpace(t))
            ThemeName = t;
    }

    public IActionResult OnPostProfile()
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            UserName = "Admin";
        }

        Response.Cookies.Append("invoxa_user", UserName.Trim(), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Path = "/"
        });

        return RedirectToPage(new { message = "Saved. Logs will now show this user name." });
    }



    public IActionResult OnPostTheme()
    {
        var allowed = new[] { "classic", "dark", "emerald", "purple", "orange" };
        var theme = (ThemeName ?? "classic").Trim().ToLowerInvariant();
        if (!allowed.Contains(theme))
            theme = "classic";

        Response.Cookies.Append("invoxa_theme", theme, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Path = "/"
        });

        return RedirectToPage(new { message = "Theme saved successfully." });
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        if (Request.Cookies.TryGetValue("invoxa_user", out var v) && !string.IsNullOrWhiteSpace(v))
            UserName = v;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = CurrentUserContext.GetEmail(HttpContext);
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToPage("/Account/Login", new { message = "Please login again." });
        }

        var user = await _db.UserAccounts.FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower() && x.IsActive);
        if (user == null)
        {
            return RedirectToPage("/Account/Login", new { message = "Your account could not be found. Please login again." });
        }

        if (!PasswordHasher.Verify(PasswordInput.CurrentPassword, user.PasswordHash))
        {
            ModelState.AddModelError("PasswordInput.CurrentPassword", "Current password is incorrect.");
            return Page();
        }

        if (PasswordHasher.Verify(PasswordInput.NewPassword, user.PasswordHash))
        {
            ModelState.AddModelError("PasswordInput.NewPassword", "New password must be different from your current password.");
            return Page();
        }

        user.PasswordHash = PasswordHasher.Hash(PasswordInput.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiryUtc = null;
        await _db.SaveChangesAsync();

        return RedirectToPage(new { message = "Password updated successfully." });
    }

    public class ChangePasswordInputModel
    {
        [Required]
        [Display(Name = "Current Password")]
        public string CurrentPassword { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 6)]
        [Display(Name = "New Password")]
        public string NewPassword { get; set; } = "";

        [Required, Compare(nameof(NewPassword))]
        [Display(Name = "Confirm New Password")]
        public string ConfirmNewPassword { get; set; } = "";
    }
}
