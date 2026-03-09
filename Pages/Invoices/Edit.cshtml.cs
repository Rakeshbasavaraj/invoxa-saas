using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Invoices;

public class EditModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPdfGenerator _pdf;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;

    public EditModel(AppDbContext db, ICurrentUser user, IPdfGenerator pdf, IEmailSender email, IConfiguration config)
    {
        _db = db;
        _user = user;
        _pdf = pdf;
        _email = email;
        _config = config;
    }

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    [BindProperty] public string InvoiceNumber { get; set; } = "";
    [BindProperty] public DateTime IssueDate { get; set; }
    [BindProperty] public DateTime DueDate { get; set; }
    [BindProperty] public InvoiceStatus Status { get; set; }
    [BindProperty] public string? Notes { get; set; }

    // Recurring (optional)
    [BindProperty] public bool IsRecurringTemplate { get; set; }
    [BindProperty] public bool RecurrenceEnabled { get; set; }
    [BindProperty] public int RecurrenceIntervalDays { get; set; } = 30;

    // Ship To (optional)
    [BindProperty] public bool ShipToSameAsBillTo { get; set; }
    [BindProperty] public string? ShipToName { get; set; }
    [BindProperty] public string? ShipToAddressLine1 { get; set; }
    [BindProperty] public string? ShipToAddressLine2 { get; set; }
    [BindProperty] public string? ShipToCity { get; set; }
    [BindProperty] public string? ShipToCountry { get; set; }

    public class InvoiceItemInput
    {
        public string? Description { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal UnitPrice { get; set; } = 0m;
    }

    [BindProperty] public List<InvoiceItemInput> Items { get; set; } = new();

    public SelectList StatusOptions { get; set; } = default!;
    public string? Message { get; set; }

    public async Task<IActionResult> OnGet()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var inv = await _db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == Id && i.CompanyId == companyId);
        if (inv is null) return NotFound();

        InvoiceNumber = inv.InvoiceNumber;
        IssueDate = inv.IssueDate.ToDateTime(TimeOnly.MinValue);
        DueDate = inv.DueDate.ToDateTime(TimeOnly.MinValue);
        Status = inv.Status;
        Notes = inv.Notes;
        IsRecurringTemplate = inv.IsRecurringTemplate;
        RecurrenceEnabled = inv.RecurrenceEnabled;
        RecurrenceIntervalDays = inv.RecurrenceIntervalDays <= 0 ? 30 : inv.RecurrenceIntervalDays;
        ShipToName = inv.ShipToName;
        ShipToAddressLine1 = inv.ShipToAddressLine1;
        ShipToAddressLine2 = inv.ShipToAddressLine2;
        ShipToCity = inv.ShipToCity;
        ShipToCountry = inv.ShipToCountry;
        ShipToSameAsBillTo = false;

        Items = inv.Items
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new InvoiceItemInput { Description = x.Description, Quantity = x.Quantity, UnitPrice = x.UnitPrice })
            .ToList();

        if (Items.Count == 0)
            Items.Add(new InvoiceItemInput { Description = "Service", Quantity = 1, UnitPrice = 0m });

        StatusOptions = new SelectList(Enum.GetValues(typeof(InvoiceStatus)).Cast<InvoiceStatus>());
        return Page();
    }

    public async Task<IActionResult> OnPost()
    {
        // Defensive: model binding can set Items to null if the posted field names are missing/wrong.
        // The Razor view uses Model.Items.Count, so always keep a non-null list.
        Items ??= new List<InvoiceItemInput>();

        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        // IMPORTANT:
        // We intentionally DO NOT load Items into the change tracker here.
        // Clearing a tracked collection can trigger per-row deletes and, in some cases,
        // EF will throw DbUpdateConcurrencyException if any dependent row was already removed.
        // For reliability, we delete invoice items with ExecuteDeleteAsync and then insert fresh rows.
        var inv = await _db.Invoices
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == Id && i.CompanyId == companyId);

        if (inv is null) return NotFound();

        var validItems = (Items ?? new List<InvoiceItemInput>())
            .Where(i => !string.IsNullOrWhiteSpace(i.Description) && i.Quantity > 0)
            .ToList();

        if (validItems.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one invoice item.");
            // Keep at least one row visible in the UI
            Items = new List<InvoiceItemInput> { new() { Description = "Service", Quantity = 1, UnitPrice = 0m } };
            StatusOptions = new SelectList(Enum.GetValues(typeof(InvoiceStatus)).Cast<InvoiceStatus>());
            return Page();
        }

        inv.IssueDate = DateOnly.FromDateTime(IssueDate);
        inv.DueDate = DateOnly.FromDateTime(DueDate);
        inv.Status = Status;
        inv.Notes = Notes;

        inv.IsRecurringTemplate = IsRecurringTemplate;
        inv.RecurrenceEnabled = RecurrenceEnabled;
        inv.RecurrenceIntervalDays = Math.Max(1, RecurrenceIntervalDays);
        if (inv.IsRecurringTemplate && inv.RecurrenceEnabled)
        {
            inv.NextOccurrenceDate ??= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(inv.RecurrenceIntervalDays);
        }
        else
        {
            inv.NextOccurrenceDate = null;
        }

        // Ship To
        if (ShipToSameAsBillTo)
        {
            var c = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == inv.ClientId && x.CompanyId == inv.CompanyId);
            if (c != null)
            {
                inv.ShipToName = c.Name;
                inv.ShipToAddressLine1 = c.AddressLine1;
                inv.ShipToAddressLine2 = c.AddressLine2;
                inv.ShipToCity = c.City;
                inv.ShipToCountry = c.Country;
            }
        }
        else
        {
            inv.ShipToName = ShipToName;
            inv.ShipToAddressLine1 = ShipToAddressLine1;
            inv.ShipToAddressLine2 = ShipToAddressLine2;
            inv.ShipToCity = ShipToCity;
            inv.ShipToCountry = ShipToCountry;
        }
        inv.UpdatedAtUtc = DateTime.UtcNow;

        // Replace items (reliable): delete then insert (no tracked collection clear)
        await _db.InvoiceItems.Where(x => x.InvoiceId == inv.Id).ExecuteDeleteAsync();
        foreach (var it in validItems)
        {
            _db.InvoiceItems.Add(new InvoiceItem
            {
                InvoiceId = inv.Id,
                Description = it.Description!.Trim(),
                Quantity = it.Quantity,
                UnitPrice = it.UnitPrice,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        _db.ReminderLogs.Add(new ReminderLog
        {
            CompanyId = companyId,
            InvoiceId = inv.Id,
            Actor = _user.Name,
            Channel = "app",
            Type = "Invoice Edited",
            To = inv.InvoiceNumber,
            Notes = "Invoice updated from Edit page"
        });

        await _db.SaveChangesAsync();

        // Reload with items for UI only (editing should not resend invoice email)
        inv = await _db.Invoices
            .Include(i => i.Items)
            .Include(i => i.Client)
            .FirstAsync(i => i.Id == Id && i.CompanyId == companyId);

        Message = "Saved.";
        // Re-hydrate bound properties so the view always has consistent data after POST.
        InvoiceNumber = inv.InvoiceNumber;
        IssueDate = inv.IssueDate.ToDateTime(TimeOnly.MinValue);
        DueDate = inv.DueDate.ToDateTime(TimeOnly.MinValue);
        Notes = inv.Notes;
        ShipToName = inv.ShipToName;
        ShipToAddressLine1 = inv.ShipToAddressLine1;
        ShipToAddressLine2 = inv.ShipToAddressLine2;
        ShipToCity = inv.ShipToCity;
        ShipToCountry = inv.ShipToCountry;
        ShipToSameAsBillTo = false;
        Items = inv.Items
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new InvoiceItemInput { Description = x.Description, Quantity = x.Quantity, UnitPrice = x.UnitPrice })
            .ToList();
        if (Items.Count == 0)
            Items.Add(new InvoiceItemInput { Description = "Service", Quantity = 1, UnitPrice = 0m });

        StatusOptions = new SelectList(Enum.GetValues(typeof(InvoiceStatus)).Cast<InvoiceStatus>());
        return Page();
    }
}