using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Invoices;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPdfGenerator _pdf;
    private readonly IEmailSender _email;
    private readonly IWhatsAppSender _wa;
    private readonly IPaymentLinkService _pay;
    private readonly IConfiguration _config;
    private readonly ICurrentUser _user;

    public DetailsModel(AppDbContext db, IPdfGenerator pdf, IEmailSender email, IWhatsAppSender wa, IPaymentLinkService pay, IConfiguration config, ICurrentUser user)
    {
        _db = db;
        _pdf = pdf;
        _email = email;
        _wa = wa;
        _pay = pay;
        _config = config;
        _user = user;
    }

    public Invoice Invoice { get; set; } = default!;
    public Company? Company { get; set; }
    public string? Message { get; set; }

    public async Task<IActionResult> OnGet(Guid id)
    {
        await LoadInvoice(id);
        return Page();
    }

    public async Task<IActionResult> OnPostDownload(Guid id)
    {
        await LoadInvoice(id);
        Company = await _db.Companies.FirstAsync(c => c.Id == Invoice.CompanyId);
        var company = Company;
        var companyName = company.Name;
        var bytes = _pdf.GenerateInvoicePdf(Invoice, company);
        return File(bytes, "application/pdf", $"{Invoice.InvoiceNumber}.pdf");
    }

    public async Task<IActionResult> OnPostSendEmail(Guid id)
    {
        await LoadInvoice(id);

        var to = Invoice.Client?.Email;
        if (string.IsNullOrWhiteSpace(to))
        {
            Message = "Client email is empty. Please add an email for this client.";
            return Page();
        }

        Company = await _db.Companies.FirstAsync(c => c.Id == Invoice.CompanyId);
        var company = Company;
        var companyName = company.Name;

        if (string.IsNullOrWhiteSpace(Invoice.PaymentLink))
        {
            var paymentUrl = await _pay.CreatePaymentLinkAsync(Invoice, company);
            if (!string.IsNullOrWhiteSpace(paymentUrl))
            {
                Invoice.PaymentLink = paymentUrl;
                Invoice.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var bytes = _pdf.GenerateInvoicePdf(Invoice, company);
        var publicUrl = $"{Request.Scheme}://{Request.Host}/i/{Invoice.PublicToken}";
        var payUrl = string.IsNullOrWhiteSpace(Invoice.PaymentLink) ? publicUrl : Invoice.PaymentLink;

        var htmlBody = BuildInvoiceEmailHtml(companyName, Invoice.Client!.Name, Invoice.InvoiceNumber, InvoiceMoney.GetGrandTotal(Invoice, company), payUrl, publicUrl);

        try
        {
            await _email.SendAsync(to,
                $"Invoice {Invoice.InvoiceNumber} from {companyName}",
                htmlBody,
                bytes,
                $"{Invoice.InvoiceNumber}.pdf");

            var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
            _db.ReminderLogs.Add(new ReminderLog { CompanyId = companyId, InvoiceId = Invoice.Id, Actor = _user.Name, Channel = "Email", Type = "InvoiceEmailSent", To = to, Notes = $"Sent invoice {Invoice.InvoiceNumber}" });
            await _db.SaveChangesAsync();

            Message = "Email sent with payment link.";
        }
        catch (Exception ex)
        {
            Message = "Email not sent. Check Company Settings SMTP or SendGrid configuration. Error: " + ex.Message;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSendWhatsApp(Guid id)
    {
        await LoadInvoice(id);

        var toPhone = Invoice.Client?.Phone;
        if (string.IsNullOrWhiteSpace(toPhone))
        {
            Message = "Client phone is empty. Please add a phone number for this client (E.164 like +965XXXXXXXX).";
            return Page();
        }

        Company = await _db.Companies.FirstAsync(c => c.Id == Invoice.CompanyId);
        var company = Company;
        var companyName = company.Name;

        if (string.IsNullOrWhiteSpace(Invoice.PaymentLink))
        {
            var paymentUrl = await _pay.CreatePaymentLinkAsync(Invoice, company);
            if (!string.IsNullOrWhiteSpace(paymentUrl))
            {
                Invoice.PaymentLink = paymentUrl;
                Invoice.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var publicUrl = $"{Request.Scheme}://{Request.Host}/i/{Invoice.PublicToken}";
        var payUrl = string.IsNullOrWhiteSpace(Invoice.PaymentLink) ? publicUrl : Invoice.PaymentLink;
        var msg = $"Invoice {Invoice.InvoiceNumber} from {companyName}. Total: {InvoiceMoney.GetGrandTotal(Invoice, company):0.00}. Status: {Invoice.Status}. Pay now: {payUrl}. View invoice: {publicUrl}";

        await _wa.SendAsync(toPhone.Trim(), msg);

        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        _db.ReminderLogs.Add(new ReminderLog
        {
            CompanyId = companyId,
            InvoiceId = Invoice.Id,
            Actor = _user.Name,
            Channel = "WhatsApp",
            Type = "InvoiceWhatsAppSent",
            To = toPhone,
            Notes = $"Sent WhatsApp for {Invoice.InvoiceNumber}"
        });
        await _db.SaveChangesAsync();

        Message = "WhatsApp sent with payment link.";
        return Page();
    }

    
    public async Task<IActionResult> OnPostMarkPaid(Guid id)
    {
        await LoadInvoice(id);
        Company = await _db.Companies.FirstAsync(c => c.Id == Invoice.CompanyId);
        var company = Company;

        Invoice.Status = InvoiceStatus.Paid;
        Invoice.PaidAtUtc = DateTime.UtcNow;
        Invoice.UpdatedAtUtc = DateTime.UtcNow;

        _db.ReminderLogs.Add(new ReminderLog
        {
            CompanyId = company.Id,
            InvoiceId = Invoice.Id,
            Actor = _user.Name,
            Channel = "System",
            Type = "MarkedPaid",
            To = Invoice.InvoiceNumber
        });

        var paidDate = Invoice.PaidAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var transactionId = $"CASH-{Invoice.Id.ToString()[..8].ToUpperInvariant()}";
        var paymentSummary = $"Payment Mode: Cash / Manual\nReference: {transactionId}\nPaid Date: {paidDate} UTC\nAmount Paid: {InvoiceMoney.FormatAmount(InvoiceMoney.GetGrandTotal(Invoice, company), company)}\nUpdated By: {_user.Name}";
        Invoice.Notes = MergePaymentSummary(Invoice.Notes, paymentSummary);

        if (!string.IsNullOrWhiteSpace(Invoice.Client?.Email))
        {
            var bytes = _pdf.GenerateInvoicePdf(Invoice, company);

            var publicUrl = $"{Request.Scheme}://{Request.Host}/i/{Invoice.PublicToken}";
            var htmlBody = BuildPaymentReceivedEmailHtml(company.Name, Invoice.Client.Name, Invoice.InvoiceNumber, InvoiceMoney.GetGrandTotal(Invoice, company), paidDate, transactionId, publicUrl);

            try
            {
                await _email.SendAsync(
                    Invoice.Client!.Email!,
                    $"Payment received for Invoice {Invoice.InvoiceNumber}",
                    htmlBody,
                    bytes,
                    $"{Invoice.InvoiceNumber}-PAID.pdf");

                _db.ReminderLogs.Add(new ReminderLog
                {
                    CompanyId = company.Id,
                    InvoiceId = Invoice.Id,
                    Actor = _user.Name,
                    Channel = "Email",
                    Type = "PaymentReceivedEmailSent",
                    To = Invoice.Client.Email,
                    Notes = $"Paid receipt email sent for {Invoice.InvoiceNumber}"
                });
            }
            catch (Exception ex)
            {
                Message = "Marked as Paid, but confirmation email was not sent. Error: " + ex.Message;
            }
        }

        await _db.SaveChangesAsync();

        if (string.IsNullOrWhiteSpace(Message)) Message = "Marked as Paid. Payment confirmation email sent.";
        return Page();
    }


    public async Task<IActionResult> OnPostMarkUnpaid(Guid id)
    {
        await LoadInvoice(id);
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);

        Invoice.Status = InvoiceStatus.Unpaid;
        Invoice.PaidAtUtc = null;
        Invoice.UpdatedAtUtc = DateTime.UtcNow;

        _db.ReminderLogs.Add(new ReminderLog { CompanyId = companyId, InvoiceId = Invoice.Id, Actor = _user.Name, Channel = "System", Type = "MarkedUnpaid", To = Invoice.InvoiceNumber });
        await _db.SaveChangesAsync();

        Message = "Marked as Unpaid.";
        return Page();
    }


    public async Task<IActionResult> OnPostCreatePaymentLink(Guid id)
    {
        await LoadInvoice(id);
        Company = await _db.Companies.FirstAsync(c => c.Id == Invoice.CompanyId);
        var company = Company;

        var url = await _pay.CreatePaymentLinkAsync(Invoice, company);
        if (string.IsNullOrWhiteSpace(url))
        {
            Message = "Payment link not created. Check Stripe settings and Command Prompt error details.";
            return Page();
        }

        Invoice.PaymentLink = url;
        Invoice.UpdatedAtUtc = DateTime.UtcNow;
        _db.ReminderLogs.Add(new ReminderLog { CompanyId = company.Id, InvoiceId = Invoice.Id, Actor = _user.Name, Channel = "System", Type = "PaymentLinkCreated", To = url, Notes = $"Payment link created for {Invoice.InvoiceNumber}" });
        await _db.SaveChangesAsync();
        Message = "Payment link created.";
        return Page();
    }

    public async Task<IActionResult> OnPostToggleRecurringTemplate(Guid id, bool enable, int intervalDays = 30)
    {
        await LoadInvoice(id);
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);

        Invoice.IsRecurringTemplate = enable;
        Invoice.RecurrenceEnabled = enable;
        Invoice.RecurrenceIntervalDays = Math.Max(1, intervalDays);
        Invoice.NextOccurrenceDate = enable ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(Invoice.RecurrenceIntervalDays) : null;
        Invoice.UpdatedAtUtc = DateTime.UtcNow;

        _db.ReminderLogs.Add(new ReminderLog { CompanyId = companyId, InvoiceId = Invoice.Id, Actor = _user.Name, Channel = "System", Type = "RecurringTemplateUpdated", To = Invoice.InvoiceNumber, Notes = enable ? $"Enabled every {Invoice.RecurrenceIntervalDays} days" : "Disabled" });
        await _db.SaveChangesAsync();

        Message = enable ? "Recurring template enabled." : "Recurring template disabled.";
        return Page();
    }


    private string BuildInvoiceEmailHtml(string companyName, string clientName, string invoiceNumber, decimal total, string payUrl, string viewUrl)
    {
        companyName = string.IsNullOrWhiteSpace(companyName) ? "Invoxa" : companyName;
        clientName = string.IsNullOrWhiteSpace(clientName) ? "Customer" : clientName;
        invoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? "Invoice" : invoiceNumber;
        payUrl = string.IsNullOrWhiteSpace(payUrl) ? viewUrl : payUrl;

        return $@"<html>
<body style='margin:0;padding:0;background:#f5f7fb;font-family:Arial,Helvetica,sans-serif;color:#0f172a;'>
  <div style='max-width:640px;margin:24px auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:16px;overflow:hidden;'>
    <div style='padding:24px 28px;background:linear-gradient(135deg,#2563eb,#1d4ed8);color:#ffffff;'>
      <div style='font-size:28px;font-weight:800;'>{companyName}</div>
      <div style='margin-top:6px;font-size:14px;opacity:0.92;'>Invoice {invoiceNumber} is ready</div>
    </div>
    <div style='padding:28px;'>
      <p style='margin:0 0 18px 0;font-size:16px;'>Hello {clientName},</p>
      <p style='margin:0 0 16px 0;font-size:15px;line-height:1.7;'>Your invoice <strong>{invoiceNumber}</strong> is ready.</p>
      <div style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:12px;padding:16px;margin:18px 0;'>
        <div style='font-size:14px;color:#64748b;'>Amount Due</div>
        <div style='font-size:28px;font-weight:800;margin-top:6px;'>{total:0.00}</div>
      </div>
      <div style='margin:24px 0 12px 0;'>
        <a href='{payUrl}' style='display:inline-block;background:#16a34a;color:#ffffff;text-decoration:none;padding:14px 22px;border-radius:10px;font-weight:700;margin-right:12px;'>Pay Now</a>
        <a href='{viewUrl}' style='display:inline-block;background:#eff6ff;color:#1d4ed8;text-decoration:none;padding:14px 22px;border-radius:10px;font-weight:700;border:1px solid #bfdbfe;'>View Invoice</a>
      </div>
      <p style='margin:18px 0 0 0;font-size:14px;color:#64748b;line-height:1.7;'>A PDF copy of the invoice is attached for your records.</p>
      <p style='margin:20px 0 0 0;font-size:14px;color:#64748b;line-height:1.7;'>Thanks,<br>{companyName}</p>
    </div>
  </div>
</body>
</html>";
    }

    private string BuildPaymentReceivedEmailHtml(string companyName, string clientName, string invoiceNumber, decimal total, string paidDate, string transactionId, string viewUrl)
    {
        companyName = string.IsNullOrWhiteSpace(companyName) ? "Invoxa" : companyName;
        clientName = string.IsNullOrWhiteSpace(clientName) ? "Customer" : clientName;
        invoiceNumber = string.IsNullOrWhiteSpace(invoiceNumber) ? "Invoice" : invoiceNumber;
        transactionId = string.IsNullOrWhiteSpace(transactionId) ? "N/A" : transactionId;

        return $@"<html>
<body style='margin:0;padding:0;background:#f5f7fb;font-family:Arial,Helvetica,sans-serif;color:#0f172a;'>
  <div style='max-width:640px;margin:24px auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:16px;overflow:hidden;'>
    <div style='padding:24px 28px;background:linear-gradient(135deg,#16a34a,#15803d);color:#ffffff;'>
      <div style='font-size:28px;font-weight:800;'>Payment Received</div>
      <div style='margin-top:6px;font-size:14px;opacity:0.92;'>Invoice {invoiceNumber}</div>
    </div>
    <div style='padding:28px;'>
      <p style='margin:0 0 18px 0;font-size:16px;'>Hello {clientName},</p>
      <p style='margin:0 0 16px 0;font-size:15px;line-height:1.7;'>We have successfully received your payment for invoice <strong>{invoiceNumber}</strong>.</p>
      <table style='width:100%;border-collapse:collapse;background:#f8fafc;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden;'>
        <tr><td style='padding:12px 16px;color:#64748b;'>Amount Paid</td><td style='padding:12px 16px;font-weight:800;text-align:right;'>{total:0.00}</td></tr>
        <tr><td style='padding:12px 16px;color:#64748b;'>Payment Date</td><td style='padding:12px 16px;font-weight:700;text-align:right;'>{paidDate}</td></tr>
        <tr><td style='padding:12px 16px;color:#64748b;'>Transaction ID</td><td style='padding:12px 16px;font-weight:700;text-align:right;'>{transactionId}</td></tr>
        <tr><td style='padding:12px 16px;color:#64748b;'>Status</td><td style='padding:12px 16px;font-weight:700;text-align:right;color:#166534;'>Paid</td></tr>
      </table>
      <div style='margin:24px 0 12px 0;'>
        <a href='{viewUrl}' style='display:inline-block;background:#16a34a;color:#ffffff;text-decoration:none;padding:14px 22px;border-radius:10px;font-weight:700;'>View Paid Invoice</a>
      </div>
      <p style='margin:18px 0 0 0;font-size:14px;color:#64748b;line-height:1.7;'>Attached paid invoice includes the payment slip / payment summary for future checking.</p>
      <p style='margin:20px 0 0 0;font-size:14px;color:#64748b;line-height:1.7;'>Thank you,<br>{companyName}</p>
    </div>
  </div>
</body>
</html>";
    }

    private static string MergePaymentSummary(string? existingNotes, string paymentSummary)
    {
        const string prefix = "---- PAYMENT SUMMARY ----";
        var baseNotes = existingNotes ?? string.Empty;
        var markerIndex = baseNotes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            baseNotes = baseNotes[..markerIndex].TrimEnd();

        if (!string.IsNullOrWhiteSpace(baseNotes))
            return baseNotes + "\n\n" + prefix + "\n" + paymentSummary.Trim();

        return prefix + "\n" + paymentSummary.Trim();
    }

    private async Task LoadInvoice(Guid id)
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var invoice = await _db.Invoices.Include(i => i.Client).Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == companyId);
        if (invoice is null) throw new InvalidOperationException("Invoice not found");
        Invoice = invoice;
        Company = await _db.Companies.FirstAsync(c => c.Id == invoice.CompanyId);
    }
}