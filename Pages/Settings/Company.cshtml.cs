using Invoxa.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Settings;

public class CompanyModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly Invoxa.Web.Services.InvoiceAutomationWorker _automationWorker;
    private readonly Invoxa.Web.Services.IPaymentLinkService _paymentLinkService;
    public CompanyModel(AppDbContext db, Invoxa.Web.Services.InvoiceAutomationWorker automationWorker, Invoxa.Web.Services.IPaymentLinkService paymentLinkService)
    {
        _db = db;
        _automationWorker = automationWorker;
        _paymentLinkService = paymentLinkService;
    }

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string? VatNumber { get; set; }
    [BindProperty] public string? AddressLine1 { get; set; }
    [BindProperty] public string? AddressLine2 { get; set; }
    [BindProperty] public string? City { get; set; }
    [BindProperty] public string? Country { get; set; }

    [BindProperty] public string? ThankYouNote { get; set; }
    [BindProperty] public string? TermsAndConditions { get; set; }

    [BindProperty] public bool TaxAuto { get; set; } = true;
    [BindProperty] public bool TaxEnabled { get; set; }
    [BindProperty] public string TaxPresetKey { get; set; } = "kuwait_vat";
    [BindProperty] public string TaxLabel { get; set; } = "Tax";
    [BindProperty] public decimal TaxRate { get; set; } = 0m;

    [BindProperty] public string InvoiceTemplateKey { get; set; } = "classic";
    [BindProperty] public IFormFile? LogoFile { get; set; }

    [BindProperty] public string InvoicePrefix { get; set; } = "INV";
    [BindProperty] public int NextInvoiceNumber { get; set; } = 1;

    [BindProperty] public string? EmailFromName { get; set; }
    [BindProperty] public string? EmailFromAddress { get; set; }
    [BindProperty] public string? SmtpHost { get; set; }
    [BindProperty] public int? SmtpPort { get; set; }
    [BindProperty] public string? SmtpUsername { get; set; }
    [BindProperty] public string? SmtpPassword { get; set; }
    [BindProperty] public bool SmtpUseSsl { get; set; } = true;

    [BindProperty] public string? StripePublishableKey { get; set; }
    [BindProperty] public string? StripeSecretKey { get; set; }
    [BindProperty] public string StripeCurrency { get; set; } = "usd";

    public bool HasLogo { get; set; }
    public bool HasSavedStripeSecret { get; set; }
    public string? Message { get; set; }

    [BindProperty] public int ReminderDaysBeforeDue { get; set; } = 2;
    [BindProperty] public int AutomationIntervalValue { get; set; } = 1;
    [BindProperty] public string AutomationIntervalUnit { get; set; } = "Minutes";
    [BindProperty] public bool OverdueReminderEnabled { get; set; } = true;
    [BindProperty] public int OverdueReminderIntervalValue { get; set; } = 1;
    [BindProperty] public string OverdueReminderIntervalUnit { get; set; } = "Days";

    public async Task OnGet()
    {
        var company = await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();
        LoadFromCompany(company);
    }

    public async Task<IActionResult> OnPostSaveCompany()
    {
        var company = await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();

        company.Name = Name;
        company.VatNumber = VatNumber;
        company.AddressLine1 = AddressLine1;
        company.AddressLine2 = AddressLine2;
        company.City = City;
        company.Country = Country;
        company.ThankYouNote = ThankYouNote;
        company.TermsAndConditions = TermsAndConditions;
        company.InvoiceTemplateKey = string.IsNullOrWhiteSpace(InvoiceTemplateKey) ? "classic" : InvoiceTemplateKey;
        company.InvoicePrefix = string.IsNullOrWhiteSpace(InvoicePrefix) ? "INV" : InvoicePrefix.Trim().ToUpperInvariant();
        company.NextInvoiceNumber = NextInvoiceNumber <= 0 ? 1 : NextInvoiceNumber;
        company.TaxAuto = TaxAuto;
        company.TaxEnabled = TaxEnabled;
        company.TaxPresetKey = string.IsNullOrWhiteSpace(TaxPresetKey) ? "kuwait_vat" : TaxPresetKey;
        company.TaxLabel = string.IsNullOrWhiteSpace(TaxLabel) ? "Tax" : TaxLabel;
        company.TaxRate = TaxRate < 0 ? 0 : TaxRate;

        if (company.TaxAuto)
        {
            ApplyTaxFromCountry(company);
        }

        if (LogoFile != null && LogoFile.Length > 0)
        {
            if (LogoFile.Length > 1_500_000)
            {
                Message = "Logo too large (max 1.5 MB). Please upload a smaller image.";
                HasLogo = company.LogoBytes != null && company.LogoBytes.Length > 0;
        HasSavedStripeSecret = !string.IsNullOrWhiteSpace(company.StripeSecretKey);
                return Page();
            }

            await using var ms = new MemoryStream();
            await LogoFile.CopyToAsync(ms);
            company.LogoBytes = ms.ToArray();
            company.LogoContentType = string.IsNullOrWhiteSpace(LogoFile.ContentType) ? "image/png" : LogoFile.ContentType;
        }

        await _db.SaveChangesAsync();
        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "Company settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveSmtp()
    {
        var company = await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();
        company.EmailFromName = string.IsNullOrWhiteSpace(EmailFromName) ? null : EmailFromName.Trim();
        company.EmailFromAddress = string.IsNullOrWhiteSpace(EmailFromAddress) ? null : EmailFromAddress.Trim();
        company.SmtpHost = string.IsNullOrWhiteSpace(SmtpHost) ? null : SmtpHost.Trim();
        company.SmtpPort = SmtpPort;
        company.SmtpUsername = string.IsNullOrWhiteSpace(SmtpUsername) ? null : SmtpUsername.Trim();
        if (!string.IsNullOrWhiteSpace(SmtpPassword))
            company.SmtpPassword = SmtpPassword.Trim();
        company.SmtpUseSsl = SmtpUseSsl;
        await _db.SaveChangesAsync();
        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "SMTP settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveStripe()
    {
        var company = await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();
        company.StripePublishableKey = string.IsNullOrWhiteSpace(StripePublishableKey) ? null : StripePublishableKey.Trim();
        if (!string.IsNullOrWhiteSpace(StripeSecretKey))
            company.StripeSecretKey = StripeSecretKey.Trim();
        company.StripeCurrency = string.IsNullOrWhiteSpace(StripeCurrency) ? "usd" : StripeCurrency.Trim().ToLowerInvariant();

        await _db.SaveChangesAsync();

        int generated = 0;
        if (!string.IsNullOrWhiteSpace(company.StripeSecretKey))
        {
            var openInvoices = await _db.Invoices
                .Include(i => i.Client)
                .Include(i => i.Items)
                .Where(i => i.CompanyId == company.Id)
                .Where(i => i.Status == Invoxa.Web.Domain.InvoiceStatus.Unpaid || i.Status == Invoxa.Web.Domain.InvoiceStatus.Overdue)
                .Where(i => i.Total > 0)
                .Where(i => string.IsNullOrWhiteSpace(i.PaymentLink))
                .ToListAsync();

            foreach (var invoice in openInvoices)
            {
                if (string.IsNullOrWhiteSpace(invoice.PublicToken))
                    invoice.PublicToken = Guid.NewGuid().ToString("N");
                var url = await _paymentLinkService.CreatePaymentLinkAsync(invoice, company);
                if (!string.IsNullOrWhiteSpace(url))
                {
                    invoice.PaymentLink = url;
                    invoice.UpdatedAtUtc = DateTime.UtcNow;
                    generated++;
                }
            }

            if (generated > 0)
                await _db.SaveChangesAsync();
        }

        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = generated > 0
            ? $"Stripe settings saved. Payment links created for {generated} open invoice(s)."
            : "Stripe settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAutomation()
    {
        var company = await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();
        company.ReminderDaysBeforeDue = ReminderDaysBeforeDue <= 0 ? 2 : ReminderDaysBeforeDue;
        company.AutomationIntervalValue = AutomationIntervalValue <= 0 ? 1 : AutomationIntervalValue;
        company.AutomationIntervalUnit = (AutomationIntervalUnit == "Hours" ? "Hours" : "Minutes");
        await _db.SaveChangesAsync();

        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "Automation settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveOverdueAutomation()
    {
        var company = await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();
        company.OverdueReminderEnabled = OverdueReminderEnabled;
        company.OverdueReminderIntervalValue = OverdueReminderIntervalValue <= 0 ? 1 : OverdueReminderIntervalValue;
        company.OverdueReminderIntervalUnit = OverdueReminderIntervalUnit == "Minutes" || OverdueReminderIntervalUnit == "Hours" ? OverdueReminderIntervalUnit : "Days";
        await _db.SaveChangesAsync();

        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "Overdue reminder settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostRunAutomationNow()
    {
        var company = await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();
        await _automationWorker.RunNow();
        await _db.Entry(company).ReloadAsync();
        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "Automation ran once now. Check Logs and your email inbox.";
        return Page();
    }

    private void LoadFromCompany(Invoxa.Web.Domain.Company company)
    {
        Name = company.Name;
        VatNumber = company.VatNumber;
        AddressLine1 = company.AddressLine1;
        AddressLine2 = company.AddressLine2;
        City = company.City;
        Country = company.Country;
        ThankYouNote = company.ThankYouNote;
        TermsAndConditions = company.TermsAndConditions;
        InvoiceTemplateKey = string.IsNullOrWhiteSpace(company.InvoiceTemplateKey) ? "classic" : company.InvoiceTemplateKey!;
        InvoicePrefix = string.IsNullOrWhiteSpace(company.InvoicePrefix) ? "INV" : company.InvoicePrefix;
        NextInvoiceNumber = company.NextInvoiceNumber <= 0 ? 1 : company.NextInvoiceNumber;
        TaxAuto = company.TaxAuto;
        TaxEnabled = company.TaxEnabled;
        TaxPresetKey = string.IsNullOrWhiteSpace(company.TaxPresetKey) ? "kuwait_vat" : company.TaxPresetKey!;
        TaxLabel = string.IsNullOrWhiteSpace(company.TaxLabel) ? "Tax" : company.TaxLabel!;
        TaxRate = company.TaxRate;
        HasLogo = company.LogoBytes != null && company.LogoBytes.Length > 0;
        HasSavedStripeSecret = !string.IsNullOrWhiteSpace(company.StripeSecretKey);
        EmailFromName = company.EmailFromName;
        EmailFromAddress = company.EmailFromAddress;
        SmtpHost = company.SmtpHost;
        SmtpPort = company.SmtpPort;
        SmtpUsername = company.SmtpUsername;
        SmtpPassword = "";
        SmtpUseSsl = company.SmtpUseSsl;
        StripePublishableKey = company.StripePublishableKey;
        StripeSecretKey = "";
        StripeCurrency = string.IsNullOrWhiteSpace(company.StripeCurrency) ? "usd" : company.StripeCurrency!;
        ReminderDaysBeforeDue = company.ReminderDaysBeforeDue <= 0 ? 2 : company.ReminderDaysBeforeDue;
        AutomationIntervalValue = company.AutomationIntervalValue <= 0 ? 1 : company.AutomationIntervalValue;
        AutomationIntervalUnit = string.IsNullOrWhiteSpace(company.AutomationIntervalUnit) ? "Minutes" : company.AutomationIntervalUnit;
        OverdueReminderEnabled = company.OverdueReminderEnabled;
        OverdueReminderIntervalValue = company.OverdueReminderIntervalValue <= 0 ? 1 : company.OverdueReminderIntervalValue;
        OverdueReminderIntervalUnit = string.IsNullOrWhiteSpace(company.OverdueReminderIntervalUnit) ? "Days" : company.OverdueReminderIntervalUnit;
    }

    private static void ApplyTaxFromCountry(Invoxa.Web.Domain.Company company)
    {
        var c = (company.Country ?? "").Trim().ToLowerInvariant();
        bool isIndia = c.Contains("india");
        bool isKuwait = c.Contains("kuwait");
        bool isUsa = c.Contains("usa") || c.Contains("united states") || c == "us" || c.Contains("america");

        if (isIndia)
        {
            company.TaxPresetKey = "india_gst";
            company.TaxEnabled = true;
            company.TaxLabel = "GST";
            company.TaxRate = 18m;
        }
        else if (isKuwait)
        {
            company.TaxPresetKey = "kuwait_vat";
            company.TaxEnabled = false;
            company.TaxLabel = "VAT";
            company.TaxRate = 0m;
        }
        else if (isUsa)
        {
            company.TaxPresetKey = "us_sales";
            company.TaxEnabled = false;
            company.TaxLabel = "Sales Tax";
            company.TaxRate = 0m;
        }
        else
        {
            company.TaxPresetKey ??= "kuwait_vat";
            company.TaxLabel = string.IsNullOrWhiteSpace(company.TaxLabel) ? "Tax" : company.TaxLabel;
            if (company.TaxRate < 0) company.TaxRate = 0m;
        }
    }
}
