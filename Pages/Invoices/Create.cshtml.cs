using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Invoices;

public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPdfGenerator _pdf;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ICurrentUser _user;

    public CreateModel(AppDbContext db, IPdfGenerator pdf, IEmailSender email, IConfiguration config, ICurrentUser user)
    {
        _db = db;
        _pdf = pdf;
        _email = email;
        _config = config;
        _user = user;
    }

    public SelectList ClientOptions { get; set; } = default!;
    public string? LimitTitle { get; set; }
    public string? LimitMessage { get; set; }

    public class InvoiceItemInput
    {
        public string? Description { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; } = 0m;
    }

    [BindProperty] public Guid ClientId { get; set; }
    [BindProperty] public string InvoiceNumber { get; set; } = "INV-";
    [BindProperty] public DateTime IssueDate { get; set; } = DateTime.Today;
    [BindProperty] public DateTime DueDate { get; set; } = DateTime.Today.AddDays(7);
    [BindProperty] public string? Notes { get; set; }

    // Ship To (optional)
    [BindProperty] public bool ShipToSameAsBillTo { get; set; }
    [BindProperty] public string? ShipToName { get; set; }
    [BindProperty] public string? ShipToAddressLine1 { get; set; }
    [BindProperty] public string? ShipToAddressLine2 { get; set; }
    [BindProperty] public string? ShipToCity { get; set; }
    [BindProperty] public string? ShipToCountry { get; set; }

    [BindProperty] public List<InvoiceItemInput> Items { get; set; } = new();

    private static string BuildInvoiceNumberPreview(Company company)
    {
        var prefix = string.IsNullOrWhiteSpace(company.InvoicePrefix) ? "INV" : company.InvoicePrefix.Trim().ToUpperInvariant();
        var next = company.NextInvoiceNumber <= 0 ? 1 : company.NextInvoiceNumber;
        return $"{prefix}-{next:0000}";
    }

    private static string BuildInvoiceNumberAndIncrement(Company company)
    {
        var prefix = string.IsNullOrWhiteSpace(company.InvoicePrefix) ? "INV" : company.InvoicePrefix.Trim().ToUpperInvariant();
        if (company.NextInvoiceNumber <= 0) company.NextInvoiceNumber = 1;
        var number = $"{prefix}-{company.NextInvoiceNumber:0000}";
        company.NextInvoiceNumber++;
        company.UpdatedAtUtc = DateTime.UtcNow;
        return number;
    }

    public async Task OnGet()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId);
        var clients = await _db.Clients.Where(c => c.CompanyId == companyId).OrderBy(c => c.Name).ToListAsync();
        ClientOptions = new SelectList(clients, nameof(Client.Id), nameof(Client.Name));
        InvoiceNumber = BuildInvoiceNumberPreview(company);

        if (Items.Count == 0)
        {
            Items.Add(new InvoiceItemInput { Description = "Service", Quantity = 1, UnitPrice = 0m });
        }
    }

    public async Task<IActionResult> OnPost()
    {
        // Defensive: model binding can set Items to null if the posted field names are missing/wrong.
        Items ??= new List<InvoiceItemInput>();

        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId);
        var currentInvoiceCount = await _db.Invoices.CountAsync(i => i.CompanyId == companyId);
        if (company.InvoiceLimit > 0 && currentInvoiceCount >= company.InvoiceLimit)
        {
            LimitTitle = $"{company.PlanKey} plan limit reached";
            LimitMessage = $"You have already created {currentInvoiceCount} of {company.InvoiceLimit} invoices allowed in the {company.PlanKey} plan. Upgrade the plan from Super Admin to continue invoicing.";
            ModelState.AddModelError(string.Empty, LimitMessage);
            var planClients = await _db.Clients.Where(c => c.CompanyId == companyId).OrderBy(c => c.Name).ToListAsync();
            ClientOptions = new SelectList(planClients, nameof(Client.Id), nameof(Client.Name));
            if (Items.Count == 0) Items.Add(new InvoiceItemInput { Description = "Service", Quantity = 1, UnitPrice = 0m });
            return Page();
        }

        var validItems = (Items ?? new List<InvoiceItemInput>())
            .Where(i => !string.IsNullOrWhiteSpace(i.Description) && i.Quantity > 0)
            .ToList();

        if (validItems.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one invoice item.");
            // Keep at least one row visible in the UI
            Items = new List<InvoiceItemInput> { new() { Description = "Service", Quantity = 1, UnitPrice = 0m } };
            var clients = await _db.Clients.Where(c => c.CompanyId == companyId).OrderBy(c => c.Name).ToListAsync();
            ClientOptions = new SelectList(clients, nameof(Client.Id), nameof(Client.Name));
            return Page();
        }

        // Always generate invoice number from Company settings (ignore user input)
        var generatedNo = BuildInvoiceNumberAndIncrement(company);

        var invoice = new Invoice
        {
            CompanyId = companyId,
            ClientId = ClientId,
            InvoiceNumber = generatedNo,
            PublicToken = Guid.NewGuid().ToString("N"),
            IssueDate = DateOnly.FromDateTime(IssueDate),
            DueDate = DateOnly.FromDateTime(DueDate),
            Status = InvoiceStatus.Unpaid,
            Notes = Notes
        };


        // Ship To
        if (ShipToSameAsBillTo)
        {
            var c = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ClientId && x.CompanyId == companyId);
            if (c != null)
            {
                invoice.ShipToName = c.Name;
                invoice.ShipToAddressLine1 = c.AddressLine1;
                invoice.ShipToAddressLine2 = c.AddressLine2;
                invoice.ShipToCity = c.City;
                invoice.ShipToCountry = c.Country;
            }
        }
        else
        {
            invoice.ShipToName = ShipToName;
            invoice.ShipToAddressLine1 = ShipToAddressLine1;
            invoice.ShipToAddressLine2 = ShipToAddressLine2;
            invoice.ShipToCity = ShipToCity;
            invoice.ShipToCountry = ShipToCountry;
        }

        foreach (var it in validItems)
        {
            invoice.Items.Add(new InvoiceItem
            {
                Description = it.Description!.Trim(),
                Quantity = it.Quantity,
                UnitPrice = it.UnitPrice
            });
        }

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync();

        _db.ReminderLogs.Add(new ReminderLog
        {
            CompanyId = companyId,
            InvoiceId = invoice.Id,
            SentAtUtc = DateTime.UtcNow,
            Actor = _user.Name,
            Channel = "app",
            Type = "Invoice Created",
            To = invoice.InvoiceNumber,
            Notes = "Invoice created successfully"
        });
        await _db.SaveChangesAsync();

        return RedirectToPage("/Invoices/Details", new { id = invoice.Id });
    }
}
