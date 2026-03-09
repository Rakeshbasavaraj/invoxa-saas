using System.ComponentModel.DataAnnotations;
using Invoxa.Web.Data;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IEmailSender _emailSender;

    public ForgotPasswordModel(AppDbContext db, IEmailSender emailSender)
    {
        _db = db;
        _emailSender = emailSender;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Message { get; set; }

    public void OnGet(string? message = null)
    {
        Message = message;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim().ToLowerInvariant();
        var user = await _db.UserAccounts.FirstOrDefaultAsync(x => x.Email.ToLower() == email && x.IsActive);

        if (user != null)
        {
            user.PasswordResetToken = Guid.NewGuid().ToString("N");
            user.PasswordResetTokenExpiryUtc = DateTime.UtcNow.AddHours(1);
            await _db.SaveChangesAsync();

            var resetUrl = Url.Page(
                "/Account/ResetPassword",
                null,
                new { token = user.PasswordResetToken },
                Request.Scheme);

            var body = $@"<p>Hello {System.Net.WebUtility.HtmlEncode(user.FullName)},</p>
<p>We received a request to reset your Invoxa password.</p>
<p><a href=""{resetUrl}"">Click here to reset your password</a></p>
<p>This link will expire in 1 hour.</p>
<p>If you did not request this, you can ignore this email.</p>";

            try
            {
                await _emailSender.SendAsync(user.Email, "Invoxa password reset", body);
            }
            catch
            {
                Message = $"Reset link generated. Email sending is not configured yet. Use this local test link: {resetUrl}";
                return Page();
            }
        }

        return RedirectToPage("/Account/Login", new { message = "If the email exists, a password reset link has been sent." });
    }

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
    }
}
