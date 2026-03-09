using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoxa.Web.Services;

namespace Invoxa.Web.Pages.Clients;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;
    public List<Client> Clients { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGet()
    {
        await LoadClientsAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid clientId)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId && c.CompanyId == companyId);
        if (client is null)
        {
            ErrorMessage = "Client not found.";
            return RedirectToPage();
        }

        var hasInvoices = await _db.Invoices.AnyAsync(i => i.ClientId == clientId && i.CompanyId == companyId);
        if (hasInvoices)
        {
            ErrorMessage = "Cannot delete client because invoices exist for this client.";
            return RedirectToPage();
        }

        _db.Clients.Remove(client);
        await _db.SaveChangesAsync();
        StatusMessage = $"Client '{client.Name}' deleted.";
        return RedirectToPage();
    }

    private async Task LoadClientsAsync()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        Clients = await _db.Clients.Where(c => c.CompanyId == companyId).OrderByDescending(c => c.CreatedAtUtc).ToListAsync();

        var changed = false;
        foreach (var c in Clients)
        {
            if (string.IsNullOrWhiteSpace(c.PortalToken))
            {
                c.PortalToken = Guid.NewGuid().ToString("N");
                changed = true;
            }
        }
        if (changed) await _db.SaveChangesAsync();
    }
}
