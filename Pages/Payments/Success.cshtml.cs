using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using DomainInvoice = Invoxa.Web.Domain.Invoice;

namespace Invoxa.Web.Pages.Payments;

public class SuccessModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IPdfGenerator _pdf;
    private readonly IEmailSender _email;

    public SuccessModel(AppDbContext db, IConfiguration cfg, IPdfGenerator pdf, IEmailSender email)
    {
        _db = db;
        _cfg = cfg;
        _pdf = pdf;
        _email = email;
    }

    public Invoxa.Web.Domain.Invoice? Invoice { get; set; }
    public string? Message { get; set; }

    public async Task<IActionResult> OnGet(string token, string? session_id = null)
    {
        Invoice = await _db.Invoices
            .Include(i => i.Client)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.PublicToken == token);

        if (Invoice == null)
        {
            Message = "Invoice not found.";
            return Page();
        }

        var company = await _db.Companies.FirstAsync(c => c.Id == Invoice.CompanyId);

        if (string.IsNullOrWhiteSpace(session_id))
        {
            Message = "Missing payment session.";
            return Page();
        }

        var secret = !string.IsNullOrWhiteSpace(company.StripeSecretKey)
            ? company.StripeSecretKey!.Trim()
            : _cfg["Stripe:SecretKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(secret))
        {
            Message = "Stripe is not configured.";
            return Page();
        }

        StripeConfiguration.ApiKey = secret;
        var sessionService = new SessionService();
        var session = await sessionService.GetAsync(session_id);

        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            Message = "Payment is not completed yet.";
            return Page();
        }

        var wasPaid = Invoice.Status == InvoiceStatus.Paid;
        Invoice.Status = InvoiceStatus.Paid;
        Invoice.PaidAtUtc ??= DateTime.UtcNow;
        Invoice.UpdatedAtUtc = DateTime.UtcNow;

        _db.ReminderLogs.Add(new ReminderLog
        {
            CompanyId = company.Id,
            InvoiceId = Invoice.Id,
            Actor = "Stripe",
            Channel = "System",
            Type = "StripeSuccess",
            To = session.Id,
            Notes = $"Stripe checkout paid for {Invoice.InvoiceNumber}"
        });

        if (!wasPaid && !string.IsNullOrWhiteSpace(Invoice.Client?.Email))
        {
            var bytes = _pdf.GenerateInvoicePdf(Invoice, company);
            var paidDate = Invoice.PaidAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
            var tx = string.IsNullOrWhiteSpace(session.PaymentIntentId) ? session.Id : session.PaymentIntentId;

            var body = $@"<html>
<body style='margin:0;padding:0;background:#f5f7fb;font-family:Arial,Helvetica,sans-serif;color:#0f172a;'>
  <div style='max-width:640px;margin:24px auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:16px;overflow:hidden;'>
    <div style='padding:24px 28px;background:linear-gradient(135deg,#16a34a,#15803d);color:#ffffff;'>
      <div style='font-size:28px;font-weight:800;'>Payment Received</div>
      <div style='margin-top:6px;font-size:14px;opacity:0.92;'>Invoice {Invoice.InvoiceNumber}</div>
    </div>
    <div style='padding:28px;'>
      <p style='margin:0 0 18px 0;font-size:16px;'>Hello {Invoice.Client.Name},</p>
      <p style='margin:0 0 16px 0;font-size:15px;line-height:1.7;'>We have successfully received your payment for invoice <strong>{Invoice.InvoiceNumber}</strong>.</p>
      <table style='width:100%;border-collapse:collapse;background:#f8fafc;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden;'>
        <tr><td style='padding:12px 16px;color:#64748b;'>Amount Paid</td><td style='padding:12px 16px;font-weight:800;text-align:right;'>{Invoice.Total:0.00}</td></tr>
        <tr><td style='padding:12px 16px;color:#64748b;'>Payment Date</td><td style='padding:12px 16px;font-weight:700;text-align:right;'>{paidDate}</td></tr>
        <tr><td style='padding:12px 16px;color:#64748b;'>Transaction ID</td><td style='padding:12px 16px;font-weight:700;text-align:right;'>{tx}</td></tr>
        <tr><td style='padding:12px 16px;color:#64748b;'>Status</td><td style='padding:12px 16px;font-weight:700;text-align:right;color:#166534;'>Paid</td></tr>
      </table>
      <p style='margin:18px 0 0 0;font-size:14px;color:#64748b;line-height:1.7;'>A paid PDF copy is attached for your records.</p>
      <p style='margin:20px 0 0 0;font-size:14px;color:#64748b;line-height:1.7;'>Thank you,<br>{company.Name}</p>
    </div>
  </div>
</body>
</html>";

            await _email.SendAsync(
                Invoice.Client.Email!,
                $"Payment received for Invoice {Invoice.InvoiceNumber}",
                body,
                bytes,
                $"{Invoice.InvoiceNumber}-PAID.pdf");

            _db.ReminderLogs.Add(new ReminderLog
            {
                CompanyId = company.Id,
                InvoiceId = Invoice.Id,
                Actor = "Stripe",
                Channel = "Email",
                Type = "PaymentReceivedEmailSent",
                To = Invoice.Client.Email,
                Notes = $"Stripe paid receipt email sent for {Invoice.InvoiceNumber}"
            });
        }

        await _db.SaveChangesAsync();
        Message = wasPaid
            ? "This invoice was already marked as paid."
            : "Payment confirmed. Invoice marked as paid and receipt email sent.";
        return Page();
    }

    public async Task<IActionResult> OnPostDownload(string token)
    {
        var inv = await _db.Invoices
            .Include(i => i.Client)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.PublicToken == token);
        if (inv == null) return NotFound();

        var company = await _db.Companies.FirstAsync(c => c.Id == inv.CompanyId);
        var bytes = _pdf.GenerateInvoicePdf(inv, company);
        return File(bytes, "application/pdf", $"{inv.InvoiceNumber}.pdf");
    }
}
