using System.ComponentModel.DataAnnotations;
using Invoxa.Web.Data;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Account;

public class ResetPasswordModel : PageModel
{
    private readonly AppDbContext _db;

    public ResetPasswordModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Message { get; set; }

    public IActionResult OnGet(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Account/Login", new { message = "Invalid password reset link." });
        }

        Input.Token = token;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _db.UserAccounts.FirstOrDefaultAsync(x =>
            x.PasswordResetToken == Input.Token &&
            x.PasswordResetTokenExpiryUtc != null &&
            x.PasswordResetTokenExpiryUtc > DateTime.UtcNow &&
            x.IsActive);

        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "This reset link is invalid or expired.");
            return Page();
        }

        user.PasswordHash = PasswordHasher.Hash(Input.Password);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiryUtc = null;
        user.LastLoginAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return RedirectToPage("/Account/Login", new { message = "Password reset successful. Please login with your new password." });
    }

    public class InputModel
    {
        [Required]
        public string Token { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 6)]
        [Display(Name = "New Password")]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password))]
        [Display(Name = "Confirm New Password")]
        public string ConfirmPassword { get; set; } = "";
    }
}
