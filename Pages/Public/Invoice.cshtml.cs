using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Public;

public class InvoiceModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPdfGenerator _pdf;
    private readonly IPaymentLinkService _pay;

    public InvoiceModel(AppDbContext db, IPdfGenerator pdf, IPaymentLinkService pay)
    {
        _db = db;
        _pdf = pdf;
        _pay = pay;
    }

    public string Token { get; set; } = "";
    public Invoice? Invoice { get; set; }
    public string CompanyName { get; set; } = "";
    public string? PaymentMessage { get; set; }

    public async Task OnGet(string token)
    {
        Token = token;
        Invoice = await _db.Invoices
            .Include(i => i.Client)
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.PublicToken == token);

        if (Invoice != null)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (Invoice.Status != InvoiceStatus.Paid && Invoice.DueDate < today && Invoice.Status != InvoiceStatus.Overdue)
            {
                Invoice.Status = InvoiceStatus.Overdue;
                Invoice.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            var company = await _db.Companies.FirstAsync(c => c.Id == Invoice.CompanyId);
            CompanyName = company.Name;

            if (!string.IsNullOrWhiteSpace(Invoice.PublicToken) &&
                string.IsNullOrWhiteSpace(Invoice.PaymentLink) &&
                Invoice.Total > 0 &&
                Invoice.Status != InvoiceStatus.Paid)
            {
                var paymentUrl = await _pay.CreatePaymentLinkAsync(Invoice, company);
                if (!string.IsNullOrWhiteSpace(paymentUrl))
                {
                    Invoice.PaymentLink = paymentUrl;
                    Invoice.UpdatedAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    PaymentMessage = "Online payment is not configured yet for this invoice. Please use the View Invoice / Download PDF options or ask the company admin to configure Stripe keys.";
                }
            }
        }
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
