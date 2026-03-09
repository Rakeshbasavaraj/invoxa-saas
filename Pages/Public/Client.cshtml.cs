using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Public;

public class ClientModel : PageModel
{
    private readonly AppDbContext _db;

    public ClientModel(AppDbContext db)
    {
        _db = db;
    }

    public string Token { get; set; } = "";
    public Client? Client { get; set; }
    public string CompanyName { get; set; } = "";
    public List<Invoice> Invoices { get; set; } = new();

    public async Task<IActionResult> OnGet(string token)
    {
        Token = token;
        Client = await _db.Clients.FirstOrDefaultAsync(c => c.PortalToken == token);
        if (Client == null) return NotFound();

        var company = await _db.Companies.FirstAsync(c => c.Id == Client.CompanyId);
        CompanyName = company.Name;

        Invoices = await _db.Invoices
            .Include(i => i.Items)
            .Where(i => i.CompanyId == Client.CompanyId && i.ClientId == Client.Id)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync();

        // Ensure public tokens exist
        var changed = false;
        foreach (var inv in Invoices)
        {
            if (string.IsNullOrWhiteSpace(inv.PublicToken))
            {
                inv.PublicToken = Guid.NewGuid().ToString("N");
                changed = true;
            }
        }
        if (changed) await _db.SaveChangesAsync();

        return Page();
    }
}
