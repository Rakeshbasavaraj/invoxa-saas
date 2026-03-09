using Invoxa.Web.Data;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Owner;

public class CompaniesModel : PageModel
{
    private readonly AppDbContext _db;
    public CompaniesModel(AppDbContext db) => _db = db;

    public List<Row> Companies { get; set; } = new();

    [TempData] public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CurrentUserContext.IsSuperAdmin(HttpContext))
            return RedirectToPage("/Index");

        Companies = await _db.Companies
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new Row
            {
                Id = c.Id,
                Name = c.Name,
                CreatedAtUtc = c.CreatedAtUtc,
                ApprovalStatus = c.ApprovalStatus,
                PlanKey = c.PlanKey,
                InvoiceLimit = c.InvoiceLimit,
                ClientLimit = c.ClientLimit,
                UserCount = _db.UserAccounts.Count(u => u.CompanyId == c.Id && u.IsActive),
                ClientCount = _db.Clients.Count(cl => cl.CompanyId == c.Id),
                InvoiceCount = _db.Invoices.Count(i => i.CompanyId == c.Id)
            })
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id)
    {
        if (!CurrentUserContext.IsSuperAdmin(HttpContext))
            return RedirectToPage("/Index");

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (company != null)
        {
            company.ApprovalStatus = "Active";
            company.ApprovedAtUtc = DateTime.UtcNow;
            company.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            StatusMessage = $"Approved {company.Name}.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSuspendAsync(Guid id)
    {
        if (!CurrentUserContext.IsSuperAdmin(HttpContext))
            return RedirectToPage("/Index");

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (company != null)
        {
            company.ApprovalStatus = "Suspended";
            company.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            StatusMessage = $"Suspended {company.Name}.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetPlanAsync(Guid id, string planKey)
    {
        if (!CurrentUserContext.IsSuperAdmin(HttpContext))
            return RedirectToPage("/Index");

        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == id);
        if (company != null)
        {
            var normalizedPlan = NormalizePlan(planKey);
            var limits = GetPlanLimits(normalizedPlan);
            company.PlanKey = normalizedPlan;
            company.InvoiceLimit = limits.InvoiceLimit;
            company.ClientLimit = limits.ClientLimit;
            company.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            StatusMessage = $"Updated {company.Name} to {company.PlanKey}.";
        }

        return RedirectToPage();
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

    public class Row
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public string PlanKey { get; set; } = string.Empty;
        public int InvoiceLimit { get; set; }
        public int ClientLimit { get; set; }
        public int UserCount { get; set; }
        public int ClientCount { get; set; }
        public int InvoiceCount { get; set; }
    }
}
