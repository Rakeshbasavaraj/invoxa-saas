using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Invoices;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPdfGenerator _pdf;
    private readonly IEmailSender _email;
    private readonly IWhatsAppSender _wa;
    private readonly InvoiceAutomationWorker _automation;
    private readonly IPaymentLinkService _pay;

    public IndexModel(AppDbContext db, ICurrentUser user, IPdfGenerator pdf, IEmailSender email, IWhatsAppSender wa, InvoiceAutomationWorker automation, IPaymentLinkService pay)
    {
        _db = db;
        _user = user;
        _pdf = pdf;
        _email = email;
        _wa = wa;
        _automation = automation;
        _pay = pay;
    }

    public List<Invoice> Invoices { get; set; } = new();
    public string? Message { get; set; }

    public string? Q { get; set; }
    public string? Status { get; set; }
    public string? Country { get; set; }

    public async Task OnGet(string? q = null, string? status = null, string? country = null)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);

        Q = q;
        Status = status;
        Country = country;

        var query = _db.Invoices.Include(i => i.Client).Include(i => i.Items)
            .Where(i => i.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(i =>
                i.InvoiceNumber.Contains(term) ||
                (i.Client != null && i.Client.Name.Contains(term)) ||
                (i.Client != null && i.Client.Email != null && i.Client.Email.Contains(term))
            );
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InvoiceStatus>(status, true, out var st))
        {
            query = query.Where(i => i.Status == st);
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            var countryTerm = country.Trim().ToLower();
            query = query.Where(i => i.Client != null && i.Client.Country != null && i.Client.Country.ToLower().Contains(countryTerm));
        }

        Invoices = await query.OrderByDescending(i => i.CreatedAtUtc).ToListAsync();

        // Ensure public tokens exist and auto-mark overdue
        var changed = false;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var inv in Invoices)
        {
            if (string.IsNullOrWhiteSpace(inv.PublicToken))
            {
                inv.PublicToken = Guid.NewGuid().ToString("N");
                changed = true;
            }
            if (inv.Status != InvoiceStatus.Paid && inv.DueDate < today && inv.Status != InvoiceStatus.Overdue)
            {
                inv.Status = InvoiceStatus.Overdue;
                inv.UpdatedAtUtc = DateTime.UtcNow;
                changed = true;
            }
        }
        if (changed)
            await _db.SaveChangesAsync();
    }

    public async Task<IActionResult> OnPostCopy(Guid id)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId);
        var currentInvoiceCount = await _db.Invoices.CountAsync(i => i.CompanyId == companyId);
        if (company.InvoiceLimit > 0 && currentInvoiceCount >= company.InvoiceLimit)
        {
            Message = $"Your {company.PlanKey} plan allows only {company.InvoiceLimit} invoices. Upgrade the plan to create more invoices.";
            await OnGet();
            return Page();
        }

        var src = await _db.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId);
        if (src is null) return NotFound();

        // Generate invoice number from company settings
        var prefix = string.IsNullOrWhiteSpace(company.InvoicePrefix) ? "INV" : company.InvoicePrefix.Trim().ToUpperInvariant();
        if (company.NextInvoiceNumber <= 0) company.NextInvoiceNumber = 1;
        var newNo = $"{prefix}-{company.NextInvoiceNumber:0000}";
        company.NextInvoiceNumber++;
        company.UpdatedAtUtc = DateTime.UtcNow;

        var inv = new Invoice
        {
            CompanyId = companyId,
            ClientId = src.ClientId,
            InvoiceNumber = newNo,
            PublicToken = Guid.NewGuid().ToString("N"),
            IssueDate = DateOnly.FromDateTime(DateTime.Today),
            DueDate = src.DueDate,
            Status = InvoiceStatus.Unpaid,
            Notes = src.Notes,
            ShipToName = src.ShipToName,
            ShipToAddressLine1 = src.ShipToAddressLine1,
            ShipToAddressLine2 = src.ShipToAddressLine2,
            ShipToCity = src.ShipToCity,
            ShipToCountry = src.ShipToCountry
        };
        foreach (var it in src.Items)
        {
            inv.Items.Add(new InvoiceItem { Description = it.Description, Quantity = it.Quantity, UnitPrice = it.UnitPrice });
        }

        _db.Invoices.Add(inv);
        _db.ReminderLogs.Add(new ReminderLog { CompanyId = companyId, InvoiceId = inv.Id, Actor = _user.Name, Channel = "System", Type = "Copied", To = newNo, Notes = $"Copied from {src.InvoiceNumber}" });
        await _db.SaveChangesAsync();

        return RedirectToPage("/Invoices/Details", new { id = inv.Id });
    }

    public async Task<IActionResult> OnPostDownload(Guid id)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var inv = await _db.Invoices.Include(i => i.Client).Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId);
        if (inv is null) return NotFound();

        var company = await _db.Companies.FirstAsync(c => c.Id == companyId);
        var bytes = _pdf.GenerateInvoicePdf(inv, company);
        return File(bytes, "application/pdf", $"{inv.InvoiceNumber}.pdf");
    }

    public async Task<IActionResult> OnPostSendEmail(Guid id)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var inv = await _db.Invoices.Include(i => i.Client).Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId);
        if (inv is null) return NotFound();

        var to = inv.Client?.Email;
        if (string.IsNullOrWhiteSpace(to))
        {
            Message = "Client email is empty. Please add an email for this client.";
            await OnGet();
            return Page();
        }

        var company = await _db.Companies.FirstAsync(c => c.Id == companyId);
        var bytes = _pdf.GenerateInvoicePdf(inv, company);

        try
        {
            await _email.SendAsync(to,
                $"Invoice {inv.InvoiceNumber} from {company.Name}",
                $"Hello {inv.Client!.Name},\n\nPlease find your invoice attached.\n\nThanks,\n{company.Name}",
                bytes,
                $"{inv.InvoiceNumber}.pdf");

            _db.ReminderLogs.Add(new ReminderLog
            {
                CompanyId = companyId,
                InvoiceId = inv.Id,
                Actor = _user.Name,
                Channel = "Email",
                Type = "InvoiceEmailSent",
                To = to,
                Notes = $"Sent invoice {inv.InvoiceNumber}"
            });
            await _db.SaveChangesAsync();

            Message = "Email sent.";
        }
        catch (Exception ex)
        {
            Message = "Email not sent. Check Company Settings SMTP or SendGrid configuration. Error: " + ex.Message;
        }
        await OnGet();
        return Page();
    }

    
public async Task<IActionResult> OnPostSendWhatsApp(Guid id)
{
    var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
    var inv = await _db.Invoices.Include(i => i.Client).Include(i => i.Items)
        .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId);
    if (inv is null) return NotFound();

    var to = inv.Client?.Phone;
    if (string.IsNullOrWhiteSpace(to))
    {
        Message = "Client phone is empty. Please add a phone number for this client (E.164 like +965XXXXXXXX).";
        await OnGet();
        return Page();
    }

    var company = await _db.Companies.FirstAsync(c => c.Id == companyId);
    var dueTxt = inv.DueDate.ToString("yyyy-MM-dd");
    var msg = "Hi " + inv.Client!.Name + ", reminder: Invoice " + inv.InvoiceNumber + " total " + inv.Total.ToString("0.00") + " is due on " + dueTxt + "." + Environment.NewLine + "Thanks, " + company.Name;

    try
    {
        await _wa.SendAsync(NormalizePhone(to), msg);

        _db.ReminderLogs.Add(new ReminderLog
        {
            CompanyId = companyId,
            InvoiceId = inv.Id,
            Actor = _user.Name,
            Channel = "WhatsApp",
            Type = "InvoiceWhatsAppSent",
            To = to,
            Notes = $"Sent WhatsApp for {inv.InvoiceNumber}"
        });
        await _db.SaveChangesAsync();
        Message = "WhatsApp sent.";
    }
    catch (Exception ex)
    {
        Message = "WhatsApp not sent. Configure Twilio in appsettings.json (Twilio:AccountSid/AuthToken/FromWhatsApp). Error: " + ex.Message;
    }

    await OnGet();
    return Page();
}

public async Task<IActionResult> OnPostSendDueWhatsAppReminders()
{
    var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
    var company = await _db.Companies.FirstAsync(c => c.Id == companyId);

    var dueLimit = DateOnly.FromDateTime(DateTime.Today.AddDays(2));

    var targets = await _db.Invoices
        .Include(i => i.Client)
        .Where(i => i.CompanyId == companyId)
        .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue)
        .Where(i => i.DueDate <= dueLimit)
        .OrderBy(i => i.DueDate)
        .ToListAsync();

    int sent = 0, skipped = 0, failed = 0;

    foreach (var inv in targets)
    {
        var to = inv.Client?.Phone;
        if (string.IsNullOrWhiteSpace(to)) { skipped++; continue; }

        var dueTxt = inv.DueDate.ToString("yyyy-MM-dd");
        var msg = "Hi " + inv.Client!.Name + ", reminder: Invoice " + inv.InvoiceNumber + " total " + inv.Total.ToString("0.00") + " is due on " + dueTxt + "." + Environment.NewLine + "Thanks, " + company.Name;

        try
        {
            await _wa.SendAsync(NormalizePhone(to), msg);

            _db.ReminderLogs.Add(new ReminderLog
            {
                CompanyId = companyId,
                InvoiceId = inv.Id,
                Actor = _user.Name,
                Channel = "WhatsApp",
                Type = "DueReminderWhatsApp",
                To = to,
                Notes = $"Due WhatsApp for {inv.InvoiceNumber} (Due {dueTxt})"
            });
            sent++;
        }
        catch
        {
            failed++;
        }
    }

    await _db.SaveChangesAsync();
    Message = $"WhatsApp due reminders complete. Sent: {sent}. Skipped (no phone): {skipped}. Failed (Twilio/config): {failed}.";
    await OnGet();
    return Page();
}

private static string NormalizePhone(string phone)
{
    var p = phone.Trim();
    if (p.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
        p = p.Substring("whatsapp:".Length);
    if (!p.StartsWith("+") && p.All(char.IsDigit))
        p = "+" + p;
    return p;
}

public async Task<IActionResult> OnPostSendDueReminders()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var dueLimit = DateOnly.FromDateTime(DateTime.Today.AddDays(2));

        var targets = await _db.Invoices
            .Include(i => i.Client)
            .Include(i => i.Items)
            .Where(i => i.CompanyId == companyId)
            .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue)
            .Where(i => i.DueDate <= dueLimit)
            .OrderBy(i => i.DueDate)
            .ToListAsync();

        int sent = 0, skipped = 0, failed = 0;
        foreach (var inv in targets)
        {
            var to = inv.Client?.Email;
            if (string.IsNullOrWhiteSpace(to)) { skipped++; continue; }

            if (string.IsNullOrWhiteSpace(inv.PaymentLink))
            {
                var paymentUrl = await _pay.CreatePaymentLinkAsync(inv, company);
                if (!string.IsNullOrWhiteSpace(paymentUrl))
                {
                    inv.PaymentLink = paymentUrl;
                    inv.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            var bytes = _pdf.GenerateInvoicePdf(inv, company);
            var dueTxt = inv.DueDate.ToString("yyyy-MM-dd");
            var subject = $"Payment reminder: {inv.InvoiceNumber} (Due {dueTxt})";
            var publicUrl = $"{Request.Scheme}://{Request.Host}/i/{inv.PublicToken}";
            var payUrl = string.IsNullOrWhiteSpace(inv.PaymentLink) ? publicUrl : inv.PaymentLink;
            var body = $"Hello {inv.Client!.Name},\n\nJust a friendly reminder that invoice {inv.InvoiceNumber} is due on {dueTxt}.\nTotal: {inv.Total:0.00}.\n\nPay now: {payUrl}\nView invoice: {publicUrl}\n\nPlease find the invoice attached.\n\nThanks,\n{company.Name}";

            try
            {
                await _email.SendAsync(to, subject, body, bytes, $"{inv.InvoiceNumber}.pdf");

                _db.ReminderLogs.Add(new ReminderLog
                {
                    CompanyId = companyId,
                    InvoiceId = inv.Id,
                    Actor = _user.Name,
                    Channel = "Email",
                    Type = "DueReminderEmail",
                    To = to,
                    Notes = $"Due reminder for {inv.InvoiceNumber} (Due {dueTxt})"
                });
                sent++;
            }
            catch
            {
                failed++;
            }
        }

        await _db.SaveChangesAsync();

        Message = $"Due reminders complete. Sent: {sent}. Skipped (no email): {skipped}. Failed (email/config): {failed}.";
        await OnGet();
        return Page();
    }

    public async Task<IActionResult> OnPostDelete(Guid id)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var inv = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId);
        if (inv is null) return NotFound();

        _db.ReminderLogs.Add(new ReminderLog
        {
            CompanyId = companyId,
            InvoiceId = inv.Id,
            Actor = _user.Name,
            Channel = "System",
            Type = "InvoiceDeleted",
            To = inv.InvoiceNumber,
            Notes = "Invoice deleted from invoice list"
        });

        _db.Invoices.Remove(inv);
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkPaid(Guid id)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var inv = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId);
        if (inv is null) return NotFound();

        inv.Status = InvoiceStatus.Paid;
        inv.UpdatedAtUtc = DateTime.UtcNow;

        _db.ReminderLogs.Add(new ReminderLog { CompanyId = companyId, InvoiceId = inv.Id, Actor = _user.Name, Channel = "System", Type = "MarkedPaid", To = inv.InvoiceNumber, Notes = "Status changed to Paid" });
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkUnpaid(Guid id)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var inv = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId);
        if (inv is null) return NotFound();

        inv.Status = InvoiceStatus.Unpaid;
        inv.UpdatedAtUtc = DateTime.UtcNow;

        _db.ReminderLogs.Add(new ReminderLog { CompanyId = companyId, InvoiceId = inv.Id, Actor = _user.Name, Channel = "System", Type = "MarkedUnpaid", To = inv.InvoiceNumber, Notes = "Status changed to Unpaid" });
        await _db.SaveChangesAsync();
        return RedirectToPage();
    }


    public async Task<IActionResult> OnPostRunAutomation()
    {
        await _automation.RunNow(CancellationToken.None);

        Message = "Automation executed. Reminders and recurring invoice checks completed.";
        await OnGet();
        return Page();
    }

    public async Task<IActionResult> OnGetExport()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var rows = await _db.Invoices
            .Include(i => i.Client)
            .Where(i => i.CompanyId == companyId)
            .OrderByDescending(i => i.IssueDate)
            .Select(i => new
            {
                Invoice = i.InvoiceNumber,
                Client = i.Client!.Name,
                IssueDate = i.IssueDate.ToString("yyyy-MM-dd"),
                DueDate = i.DueDate.ToString("yyyy-MM-dd"),
                Status = i.Status.ToString(),
                Total = i.Total
            })
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Invoices");
        ws.Cell(1, 1).Value = "Invoice";
        ws.Cell(1, 2).Value = "Client";
        ws.Cell(1, 3).Value = "Issue Date";
        ws.Cell(1, 4).Value = "Due Date";
        ws.Cell(1, 5).Value = "Status";
        ws.Cell(1, 6).Value = "Total";

        for (int i = 0; i < rows.Count; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].Invoice;
            ws.Cell(i + 2, 2).Value = rows[i].Client;
            ws.Cell(i + 2, 3).Value = rows[i].IssueDate;
            ws.Cell(i + 2, 4).Value = rows[i].DueDate;
            ws.Cell(i + 2, 5).Value = rows[i].Status;
            ws.Cell(i + 2, 6).Value = rows[i].Total;
        }

        ws.Columns().AdjustToContents();

        using var ms = new System.IO.MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();
        var fileName = $"invoxa-invoices-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

}
