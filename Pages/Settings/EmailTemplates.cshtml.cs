using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Settings;

public class EmailTemplatesModel : PageModel
{
    private readonly AppDbContext _db;
    public EmailTemplatesModel(AppDbContext db) => _db = db;

    [BindProperty] public string InvoiceSentSubject { get; set; } = "";
    [BindProperty] public string InvoiceSentBody { get; set; } = "";

    [BindProperty] public string UpcomingDueSubject { get; set; } = "";
    [BindProperty] public string UpcomingDueBody { get; set; } = "";

    [BindProperty] public string DueTodaySubject { get; set; } = "";
    [BindProperty] public string DueTodayBody { get; set; } = "";

    [BindProperty] public string OverdueSubject { get; set; } = "";
    [BindProperty] public string OverdueBody { get; set; } = "";

    [BindProperty] public string PaymentReceivedSubject { get; set; } = "";
    [BindProperty] public string PaymentReceivedBody { get; set; } = "";

    public string? Message { get; set; }

    public async Task OnGet()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPost()
    {
        var companyId = await _db.Companies.OrderBy(c => c.CreatedAtUtc).Select(c => c.Id).FirstAsync();

        await Upsert(companyId, "InvoiceSent", InvoiceSentSubject, InvoiceSentBody);
        await Upsert(companyId, "UpcomingDue", UpcomingDueSubject, UpcomingDueBody);
        await Upsert(companyId, "DueToday", DueTodaySubject, DueTodayBody);
        await Upsert(companyId, "Overdue", OverdueSubject, OverdueBody);
        await Upsert(companyId, "PaymentReceived", PaymentReceivedSubject, PaymentReceivedBody);

        await _db.SaveChangesAsync();
        Message = "Email templates saved.";
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var companyId = await _db.Companies.OrderBy(c => c.CreatedAtUtc).Select(c => c.Id).FirstAsync();
        var templates = await _db.EmailTemplates.Where(x => x.CompanyId == companyId).ToListAsync();

        var invoiceSent = templates.First(x => x.TemplateKey == "InvoiceSent");
        InvoiceSentSubject = invoiceSent.Subject; InvoiceSentBody = invoiceSent.Body;

        var upcoming = templates.First(x => x.TemplateKey == "UpcomingDue");
        UpcomingDueSubject = upcoming.Subject; UpcomingDueBody = upcoming.Body;

        var dueToday = templates.First(x => x.TemplateKey == "DueToday");
        DueTodaySubject = dueToday.Subject; DueTodayBody = dueToday.Body;

        var overdue = templates.First(x => x.TemplateKey == "Overdue");
        OverdueSubject = overdue.Subject; OverdueBody = overdue.Body;

        var received = templates.First(x => x.TemplateKey == "PaymentReceived");
        PaymentReceivedSubject = received.Subject; PaymentReceivedBody = received.Body;
    }

    private async Task Upsert(Guid companyId, string key, string subject, string body)
    {
        var item = await _db.EmailTemplates.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.TemplateKey == key);
        if (item == null)
        {
            _db.EmailTemplates.Add(new EmailTemplate
            {
                CompanyId = companyId,
                TemplateKey = key,
                Subject = subject ?? "",
                Body = body ?? "",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            item.Subject = subject ?? "";
            item.Body = body ?? "";
            item.UpdatedAtUtc = DateTime.UtcNow;
        }
    }
}
