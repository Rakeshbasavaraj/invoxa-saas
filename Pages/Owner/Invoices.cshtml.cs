using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Owner;

public class InvoicesModel : PageModel
{
    private readonly AppDbContext _db;
    public InvoicesModel(AppDbContext db) => _db = db;

    public int DraftCount { get; set; }
    public int UnpaidCount { get; set; }
    public int PaidCount { get; set; }
    public List<Row> Invoices { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CurrentUserContext.IsSuperAdmin(HttpContext))
            return RedirectToPage("/Index");

        DraftCount = await _db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Draft);
        UnpaidCount = await _db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue);
        PaidCount = await _db.Invoices.CountAsync(i => i.Status == InvoiceStatus.Paid);

        Invoices = await (from i in _db.Invoices.AsNoTracking()
                          join c in _db.Companies.AsNoTracking() on i.CompanyId equals c.Id
                          join cl in _db.Clients.AsNoTracking() on i.ClientId equals cl.Id
                          orderby i.CreatedAtUtc descending
                          select new Row
                          {
                              InvoiceNumber = i.InvoiceNumber,
                              CompanyName = c.Name,
                              ClientName = cl.Name,
                              Amount = i.Items.Sum(x => x.Quantity * x.UnitPrice),
                              Status = i.Status.ToString(),
                              CreatedAtUtc = i.CreatedAtUtc
                          })
                          .Take(50)
                          .ToListAsync();

        return Page();
    }

    public class Row
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }
}
