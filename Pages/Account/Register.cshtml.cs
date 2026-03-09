using System.ComponentModel.DataAnnotations;
using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly AppDbContext _db;
    public RegisterModel(AppDbContext db) => _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IActionResult OnGet()
    {
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Input.AcceptTerms)
        {
            ModelState.AddModelError("Input.AcceptTerms", "Please accept terms.");
        }

        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim().ToLowerInvariant();
        if (await _db.UserAccounts.AnyAsync(x => x.Email.ToLower() == email))
        {
            ModelState.AddModelError(string.Empty, "This email is already registered.");
            return Page();
        }

        var selectedPlan = NormalizePlan(Input.PlanKey);
        var limits = GetPlanLimits(selectedPlan);

        var company = new Company
        {
            Name = Input.CompanyName.Trim(),
            TimeZone = "Asia/Kuwait",
            ApprovalStatus = "Pending",
            PlanKey = selectedPlan,
            InvoiceLimit = limits.InvoiceLimit,
            ClientLimit = limits.ClientLimit,
            InvoiceTemplateKey = "classic",
            InvoicePrefix = "INV",
            NextInvoiceNumber = 1,
            TaxEnabled = false,
            TaxPresetKey = "kuwait_vat",
            TaxLabel = "VAT",
            TaxRate = 0m,
            CreatedAtUtc = DateTime.UtcNow,
            ThankYouNote = "Thanks for your business.",
            TermsAndConditions = "Full payment is due upon receipt of this invoice."
        };

        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        var user = new UserAccount
        {
            CompanyId = company.Id,
            FullName = Input.FullName.Trim(),
            Email = email,
            PasswordHash = Services.PasswordHasher.Hash(Input.Password),
            Role = "Admin",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.UserAccounts.Add(user);
        await _db.SaveChangesAsync();

        var expires = DateTimeOffset.UtcNow.AddDays(30);
        var cookieOptions = new CookieOptions
        {
            Expires = expires,
            IsEssential = true,
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Path = "/"
        };

        return RedirectToPage("/Account/Login", new { message = "Registration successful. Your company is pending super admin approval before first login." });
    }

    private static string NormalizePlan(string? planKey)
    {
        var value = (planKey ?? "Free").Trim().ToLowerInvariant();
        return value switch
        {
            "starter" => "Starter",
            "pro" => "Pro",
            _ => "Free"
        };
    }

    private static (int InvoiceLimit, int ClientLimit) GetPlanLimits(string planKey)
    {
        return planKey switch
        {
            "Starter" => (100, 50),
            "Pro" => (999999, 999999),
            _ => (10, 5)
        };
    }

    public class InputModel
    {
        [Required, StringLength(100)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = "";

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password))]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = "";

        [Required, StringLength(120)]
        [Display(Name = "Company Name")]
        public string CompanyName { get; set; } = "";

        [Required]
        [Display(Name = "Plan")]
        public string PlanKey { get; set; } = "Free";

        public bool AcceptTerms { get; set; }
    }
}
