using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Invoxa.Web.Services;

namespace Invoxa.Web.Pages.Clients;

public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    public CreateModel(AppDbContext db) => _db = db;

    public string? LimitTitle { get; set; }
    public string? LimitMessage { get; set; }

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Phone { get; set; }

    // Address (optional)
    [BindProperty] public string? AddressLine1 { get; set; }
    [BindProperty] public string? AddressLine2 { get; set; }
    [BindProperty] public string? City { get; set; }
    [BindProperty] public string? Country { get; set; }

    public async Task<IActionResult> OnPost()
    {
        Name = (Name ?? "").Trim();
        Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
        Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim();

        if (string.IsNullOrWhiteSpace(Name))
            ModelState.AddModelError(nameof(Name), "Client name is required.");

        if (!string.IsNullOrWhiteSpace(Email) && !new EmailAddressAttribute().IsValid(Email))
            ModelState.AddModelError(nameof(Email), "Invalid email address.");

        if (!string.IsNullOrWhiteSpace(Phone))
        {
            // Accept +countrycode... or digits/spaces/hyphen (simple)
            var cleaned = Regex.Replace(Phone, @"[\s\-]", "");
            var ok = Regex.IsMatch(cleaned, @"^(\+?[0-9]{7,15})$");
            if (!ok)
                ModelState.AddModelError(nameof(Phone), "Invalid phone number. Use digits with optional +country code (7-15 digits).");
        }

        if (!ModelState.IsValid)
            return Page();

        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId);
        var currentClientCount = await _db.Clients.CountAsync(c => c.CompanyId == companyId);
        if (company.ClientLimit > 0 && currentClientCount >= company.ClientLimit)
        {
            LimitTitle = $"{company.PlanKey} plan limit reached";
            LimitMessage = $"You have already used {currentClientCount} of {company.ClientLimit} clients allowed in the {company.PlanKey} plan. Upgrade the plan from Super Admin to add more clients.";
            ModelState.AddModelError(string.Empty, LimitMessage);
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Email))
        {
            var exists = await _db.Clients.AnyAsync(c => c.CompanyId == companyId && c.Email != null && c.Email.ToLower() == Email.ToLower());
            if (exists)
            {
                ModelState.AddModelError(nameof(Email), "This email is already used by another client.");
                return Page();
            }
        }

        _db.Clients.Add(new Client
        {
            CompanyId = companyId,
            Name = Name,
            Email = Email,
            Phone = Phone,
            PortalToken = Guid.NewGuid().ToString("N"),
            AddressLine1 = AddressLine1,
            AddressLine2 = AddressLine2,
            City = City,
            Country = Country
        });
        await _db.SaveChangesAsync();
        return RedirectToPage("/Clients/Index");
    }
}