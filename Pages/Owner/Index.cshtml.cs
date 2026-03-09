using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Owner;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public int TotalCompanies { get; set; }
    public int TotalUsers { get; set; }
    public int TotalInvoices { get; set; }
    public int PendingCompanies { get; set; }
    public int ActiveCompanies { get; set; }
    public decimal PaidRevenue { get; set; }
    public List<CompanyRow> RecentCompanies { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CurrentUserContext.IsSuperAdmin(HttpContext))
            return RedirectToPage("/Index");

        TotalCompanies = await _db.Companies.CountAsync();
        TotalUsers = await _db.UserAccounts.CountAsync(x => x.IsActive);
        TotalInvoices = await _db.Invoices.CountAsync();
        PendingCompanies = await _db.Companies.CountAsync(x => x.ApprovalStatus == "Pending");
        ActiveCompanies = await _db.Companies.CountAsync(x => x.ApprovalStatus == "Active");
        PaidRevenue = await _db.Invoices
            .Where(x => x.Status == InvoiceStatus.Paid)
            .SelectMany(x => x.Items)
            .SumAsync(x => (decimal?)x.Quantity * x.UnitPrice) ?? 0m;

        RecentCompanies = await _db.Companies
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(10)
            .Select(c => new CompanyRow
            {
                Name = c.Name,
                CreatedAtUtc = c.CreatedAtUtc,
                UserCount = _db.UserAccounts.Count(u => u.CompanyId == c.Id && u.IsActive),
                InvoiceCount = _db.Invoices.Count(i => i.CompanyId == c.Id)
            })
            .ToListAsync();

        return Page();
    }

    public class CompanyRow
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public int UserCount { get; set; }
        public int InvoiceCount { get; set; }
    }
}
