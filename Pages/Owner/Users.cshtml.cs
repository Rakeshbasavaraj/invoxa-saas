using Invoxa.Web.Data;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Owner;

public class UsersModel : PageModel
{
    private readonly AppDbContext _db;
    public UsersModel(AppDbContext db) => _db = db;

    public List<Row> Users { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CurrentUserContext.IsSuperAdmin(HttpContext))
            return RedirectToPage("/Index");

        Users = await (from u in _db.UserAccounts.AsNoTracking()
                       join c in _db.Companies.AsNoTracking() on u.CompanyId equals c.Id
                       orderby u.CreatedAtUtc descending
                       select new Row
                       {
                           FullName = u.FullName,
                           Email = u.Email,
                           CompanyName = c.Name,
                           Role = u.Role,
                           LastLoginAtUtc = u.LastLoginAtUtc
                       }).ToListAsync();

        return Page();
    }

    public class Row
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public DateTime? LastLoginAtUtc { get; set; }
    }
}
