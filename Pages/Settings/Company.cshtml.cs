using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoxa.Web.Pages.Settings;

public class CompanyModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly InvoiceAutomationWorker _automationWorker;
    private readonly IPaymentLinkService _paymentLinkService;
    private readonly ILogger<CompanyModel> _logger;

    public CompanyModel(AppDbContext db, InvoiceAutomationWorker automationWorker, IPaymentLinkService paymentLinkService, ILogger<CompanyModel> logger)
    {
        _db = db;
        _automationWorker = automationWorker;
        _paymentLinkService = paymentLinkService;
        _logger = logger;
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
    [BindProperty] public decimal TaxRate { get; set; }

    [BindProperty] public string InvoiceTemplateKey { get; set; } = "default_modern";
    [BindProperty] public IFormFile? LogoFile { get; set; }
    [BindProperty] public int? LogoWidth { get; set; } = 160;
    [BindProperty] public int? LogoHeight { get; set; }
    [BindProperty] public string LogoFitMode { get; set; } = "contain";

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

    [BindProperty] public int ReminderDaysBeforeDue { get; set; } = 2;
    [BindProperty] public int AutomationIntervalValue { get; set; } = 1;
    [BindProperty] public string AutomationIntervalUnit { get; set; } = "Minutes";
    [BindProperty] public bool OverdueReminderEnabled { get; set; } = true;
    [BindProperty] public int OverdueReminderIntervalValue { get; set; } = 1;
    [BindProperty] public string OverdueReminderIntervalUnit { get; set; } = "Days";

    public bool HasLogo { get; set; }
    public bool HasSavedStripeSecret { get; set; }
    public string? StripePublishablePreview { get; set; }
    public string? StripeSecretPreview { get; set; }
    public string? Message { get; set; }

    public async Task OnGet()
    {
        var company = await GetCompanyAsync();
        LoadFromCompany(company);
    }

    public async Task<IActionResult> OnPostSaveCompany()
    {
        var company = await GetCompanyAsync();

        company.Name = Name.Trim();
        company.VatNumber = Clean(VatNumber);
        company.AddressLine1 = Clean(AddressLine1);
        company.AddressLine2 = Clean(AddressLine2);
        company.City = Clean(City);
        company.Country = Clean(Country);
        company.ThankYouNote = Clean(ThankYouNote);
        company.TermsAndConditions = Clean(TermsAndConditions);
        // Keep template selection managed from the PDF Templates page so uploading a logo here
        // does not accidentally reset the active template.
        company.InvoicePrefix = string.IsNullOrWhiteSpace(InvoicePrefix) ? "INV" : InvoicePrefix.Trim().ToUpperInvariant();
        company.NextInvoiceNumber = NextInvoiceNumber <= 0 ? 1 : NextInvoiceNumber;

        company.LogoWidth = LogoWidth is > 0 ? Math.Clamp(LogoWidth.Value, 60, 320) : 160;
        company.LogoHeight = LogoHeight is > 0 ? Math.Clamp(LogoHeight.Value, 20, 180) : null;
        company.LogoFitMode = NormalizeFitMode(LogoFitMode);

        company.TaxAuto = TaxAuto;
        company.TaxEnabled = TaxEnabled;
        company.TaxPresetKey = string.IsNullOrWhiteSpace(TaxPresetKey) ? "kuwait_vat" : TaxPresetKey.Trim().ToLowerInvariant();
        company.TaxLabel = string.IsNullOrWhiteSpace(TaxLabel) ? "Tax" : TaxLabel.Trim();
        company.TaxRate = TaxRate < 0 ? 0 : TaxRate;

        if (company.TaxAuto)
            ApplyTaxFromCountry(company);
        else if (!company.TaxEnabled)
            company.TaxRate = 0m;

        if (LogoFile is { Length: > 0 })
        {
            if (LogoFile.Length > 1_500_000)
            {
                LoadFromCompany(company);
                Message = "Logo too large (max 1.5 MB). Please upload a smaller image.";
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
        var company = await GetCompanyAsync();
        company.EmailFromName = Clean(EmailFromName);
        company.EmailFromAddress = Clean(EmailFromAddress);
        company.SmtpHost = Clean(SmtpHost);
        company.SmtpPort = SmtpPort;
        company.SmtpUsername = Clean(SmtpUsername);
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
        Company? company = null;

        try
        {
            company = await GetCompanyAsync();

            var publishableKey = Clean(StripePublishableKey);
            var secretKey = Clean(StripeSecretKey);

            if (!string.IsNullOrWhiteSpace(publishableKey))
                company.StripePublishableKey = publishableKey;
            else if (string.IsNullOrWhiteSpace(company.StripePublishableKey))
                company.StripePublishableKey = null;

            if (!string.IsNullOrWhiteSpace(secretKey))
                company.StripeSecretKey = secretKey;

            company.StripeCurrency = NormalizeStripeCurrency(StripeCurrency);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while saving Stripe settings for company settings page");

            if (company is not null)
                LoadFromCompany(company);

            SmtpPassword = "";
            StripeSecretKey = "";
            Message = "Stripe settings could not be saved. Please verify the keys and try again. Check terminal logs for details.";
            return Page();
        }

        // Do not bulk-generate payment links here.
        // Links are created on demand from invoice/public pages, which keeps settings save fast and avoids
        // unexpected runtime errors after the keys are already persisted successfully.
        LoadFromCompany(company!);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "Stripe settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAutomation()
    {
        var company = await GetCompanyAsync();
        company.ReminderDaysBeforeDue = ReminderDaysBeforeDue <= 0 ? 2 : ReminderDaysBeforeDue;
        company.AutomationIntervalValue = AutomationIntervalValue <= 0 ? 1 : AutomationIntervalValue;
        company.AutomationIntervalUnit = AutomationIntervalUnit == "Hours" ? "Hours" : "Minutes";
        await _db.SaveChangesAsync();

        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "Automation settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostSaveOverdueAutomation()
    {
        var company = await GetCompanyAsync();
        company.OverdueReminderEnabled = OverdueReminderEnabled;
        company.OverdueReminderIntervalValue = OverdueReminderIntervalValue <= 0 ? 1 : OverdueReminderIntervalValue;
        company.OverdueReminderIntervalUnit = OverdueReminderIntervalUnit is "Minutes" or "Hours" ? OverdueReminderIntervalUnit : "Days";
        await _db.SaveChangesAsync();

        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "Overdue reminder settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostRunAutomationNow()
    {
        var company = await GetCompanyAsync();
        await _automationWorker.RunNow();
        await _db.Entry(company).ReloadAsync();
        LoadFromCompany(company);
        SmtpPassword = "";
        StripeSecretKey = "";
        Message = "Automation ran once now. Check Logs and your email inbox.";
        return Page();
    }

    private async Task<Company> GetCompanyAsync()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
        if (company != null)
            return company;

        return await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();
    }

    private void LoadFromCompany(Company company)
    {
        Name = company.Name;
        VatNumber = company.VatNumber;
        AddressLine1 = company.AddressLine1;
        AddressLine2 = company.AddressLine2;
        City = company.City;
        Country = company.Country;
        ThankYouNote = company.ThankYouNote;
        TermsAndConditions = company.TermsAndConditions;
        InvoiceTemplateKey = string.IsNullOrWhiteSpace(company.InvoiceTemplateKey) ? "default_modern" : company.InvoiceTemplateKey!;
        InvoicePrefix = string.IsNullOrWhiteSpace(company.InvoicePrefix) ? "INV" : company.InvoicePrefix;
        NextInvoiceNumber = company.NextInvoiceNumber <= 0 ? 1 : company.NextInvoiceNumber;

        TaxAuto = company.TaxAuto;
        TaxEnabled = company.TaxEnabled;
        TaxPresetKey = string.IsNullOrWhiteSpace(company.TaxPresetKey) ? "kuwait_vat" : company.TaxPresetKey!;
        TaxLabel = string.IsNullOrWhiteSpace(company.TaxLabel) ? "Tax" : company.TaxLabel!;
        TaxRate = company.TaxRate;

        HasLogo = company.LogoBytes is { Length: > 0 };
        LogoWidth = company.LogoWidth is > 0 ? company.LogoWidth : 160;
        LogoHeight = company.LogoHeight;
        LogoFitMode = string.IsNullOrWhiteSpace(company.LogoFitMode) ? "contain" : company.LogoFitMode!;

        HasSavedStripeSecret = !string.IsNullOrWhiteSpace(company.StripeSecretKey);
        StripePublishablePreview = MaskStripeKey(company.StripePublishableKey, "pk");
        StripeSecretPreview = MaskStripeKey(company.StripeSecretKey, "sk");
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


    private static string? MaskStripeKey(string? value, string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length <= 8)
            return trimmed;

        var tail = trimmed.Length >= 4 ? trimmed[^4..] : trimmed;
        return $"{prefix}_********{tail}";
    }

    private static void ApplyTaxFromCountry(Company company)
    {
        var country = (company.Country ?? string.Empty).Trim().ToLowerInvariant();

        if (country.Contains("india"))
        {
            company.TaxPresetKey = "india_gst";
            company.TaxEnabled = true;
            company.TaxLabel = "GST";
            company.TaxRate = 18m;
            company.DefaultCurrency = "INR";
            company.StripeCurrency = "inr";
            company.ShowHsnSac = true;
            company.ShowSgst = true;
            company.ShowCgst = true;
            company.ShowIgst = false;
            company.ShowCess = false;
        }
        else if (country.Contains("kuwait"))
        {
            company.TaxPresetKey = "kuwait_vat";
            company.TaxEnabled = false;
            company.TaxLabel = "VAT";
            company.TaxRate = 0m;
            company.DefaultCurrency = "KWD";
            company.StripeCurrency = "kwd";
            company.ShowHsnSac = false;
            company.ShowSgst = false;
            company.ShowCgst = false;
            company.ShowIgst = false;
            company.ShowCess = false;
        }
        else if (country.Contains("usa") || country.Contains("united states") || country == "us" || country.Contains("america"))
        {
            company.TaxPresetKey = "us_sales";
            company.TaxEnabled = true;
            company.TaxLabel = "Sales Tax";
            company.TaxRate = 8m;
            company.DefaultCurrency = "USD";
            company.StripeCurrency = "usd";
            company.ShowHsnSac = false;
            company.ShowSgst = false;
            company.ShowCgst = false;
            company.ShowIgst = false;
            company.ShowCess = false;
        }
        else
        {
            company.TaxPresetKey ??= "kuwait_vat";
            company.TaxLabel = string.IsNullOrWhiteSpace(company.TaxLabel) ? "Tax" : company.TaxLabel;
            if (company.TaxRate < 0) company.TaxRate = 0m;
        }
    }

    private static string NormalizeStripeCurrency(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "usd" : value.Trim().ToLowerInvariant();
        return normalized.Length is >= 3 and <= 10 ? normalized : "usd";
    }

    private static string NormalizeFitMode(string? value)
    {
        var v = (value ?? "contain").Trim().ToLowerInvariant();
        return v is "fit" or "fitarea" ? "fit" : "contain";
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
