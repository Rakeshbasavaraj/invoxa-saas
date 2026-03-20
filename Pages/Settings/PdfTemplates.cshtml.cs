using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Pages.Settings;

public class PdfTemplatesModel : PageModel
{
    private readonly AppDbContext _db;
    public PdfTemplatesModel(AppDbContext db) => _db = db;

    [BindProperty] public string InvoiceTemplateKey { get; set; } = "default_modern";
    [BindProperty] public string TemplateDisplayName { get; set; } = "Default Modern";
    [BindProperty] public string DefaultCurrency { get; set; } = "INR";
    [BindProperty] public string PrimaryColor { get; set; } = "#2563eb";
    [BindProperty] public string TableHeaderColor { get; set; } = "#1d4ed8";
    [BindProperty] public int PdfFontSize { get; set; } = 11;
    [BindProperty] public string PdfTitleStyle { get; set; } = "Bold";
    [BindProperty] public bool ShowHsnSac { get; set; }
    [BindProperty] public bool ShowSgst { get; set; }
    [BindProperty] public bool ShowCgst { get; set; }
    [BindProperty] public bool ShowIgst { get; set; }
    [BindProperty] public bool ShowCess { get; set; }
    [BindProperty] public bool ShowTerms { get; set; } = true;
    [BindProperty] public bool ShowNotes { get; set; } = true;
    [BindProperty] public bool ShowPaymentDetails { get; set; }
    [BindProperty] public bool ShowSignature { get; set; }
    [BindProperty] public string? SignatureLabel { get; set; } = "Authorized Signature";
    [BindProperty] public string? SignatureName { get; set; }
    [BindProperty] public string? PaymentDetails { get; set; }
    [BindProperty] public string? CustomColumn1Name { get; set; } = "Item";
    [BindProperty] public string? CustomColumn2Name { get; set; } = "Qty";
    [BindProperty] public string? CustomColumn3Name { get; set; } = "Unit Price";
    [BindProperty] public string? CustomColumn4Name { get; set; } = "Total";
    [BindProperty] public bool TaxAuto { get; set; } = true;
    [BindProperty] public bool TaxEnabled { get; set; }
    [BindProperty] public string TaxPresetKey { get; set; } = "kuwait_vat";
    [BindProperty] public string TaxLabel { get; set; } = "Tax";
    [BindProperty] public decimal TaxRate { get; set; }
    [BindProperty] public string? Country { get; set; }

    public string CompanyName { get; set; } = "Your Company";
    public bool HasLogo { get; set; }
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        var company = await GetCompanyAsync();
        Load(company);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var company = await GetCompanyAsync();

        company.InvoiceTemplateKey = NormalizeTemplateKey(InvoiceTemplateKey);
        company.TemplateDisplayName = GetTemplateDisplayName(company.InvoiceTemplateKey);
        company.DefaultCurrency = NormalizeCurrency(DefaultCurrency);
        company.StripeCurrency = company.DefaultCurrency.ToLowerInvariant();
        company.PrimaryColor = NormalizeColor(PrimaryColor, GetDefaultPrimary(company.InvoiceTemplateKey));
        company.TableHeaderColor = NormalizeColor(TableHeaderColor, GetDefaultHeader(company.InvoiceTemplateKey));
        company.PdfFontSize = Math.Clamp(PdfFontSize, 9, 16);
        company.PdfTitleStyle = string.IsNullOrWhiteSpace(PdfTitleStyle) ? "Bold" : PdfTitleStyle.Trim();
        company.ShowHsnSac = ShowHsnSac;
        company.ShowSgst = ShowSgst;
        company.ShowCgst = ShowCgst;
        company.ShowIgst = ShowIgst;
        company.ShowCess = ShowCess;
        company.ShowTerms = ShowTerms;
        company.ShowNotes = ShowNotes;
        company.ShowPaymentDetails = ShowPaymentDetails;
        company.ShowSignature = ShowSignature;
        company.SignatureLabel = string.IsNullOrWhiteSpace(SignatureLabel) ? "Authorized Signature" : SignatureLabel.Trim();
        company.SignatureName = string.IsNullOrWhiteSpace(SignatureName) ? null : SignatureName.Trim();
        company.PaymentDetails = string.IsNullOrWhiteSpace(PaymentDetails) ? null : PaymentDetails.Trim();
        company.CustomColumn1Name = NormalizeColumn(CustomColumn1Name, "Item");
        company.CustomColumn2Name = NormalizeColumn(CustomColumn2Name, "Qty");
        company.CustomColumn3Name = NormalizeColumn(CustomColumn3Name, "Unit Price");
        company.CustomColumn4Name = NormalizeColumn(CustomColumn4Name, "Total");
        company.TaxAuto = TaxAuto;
        company.TaxEnabled = TaxEnabled;
        company.TaxPresetKey = string.IsNullOrWhiteSpace(TaxPresetKey) ? "kuwait_vat" : TaxPresetKey.Trim().ToLowerInvariant();
        company.TaxLabel = string.IsNullOrWhiteSpace(TaxLabel) ? "Tax" : TaxLabel.Trim();
        company.TaxRate = TaxRate < 0 ? 0 : TaxRate;
        company.Country = string.IsNullOrWhiteSpace(Country) ? null : Country.Trim();

        ApplyPreset(company);
        await _db.SaveChangesAsync();
        Load(company);
        Message = "PDF template settings saved.";
        return Page();
    }

    public async Task<IActionResult> OnPostUseTemplateAsync(string key)
    {
        var company = await GetCompanyAsync();
        key = NormalizeTemplateKey(key);
        ApplyTemplateDefaults(company, key);
        ApplyPreset(company);
        await _db.SaveChangesAsync();
        Load(company);
        Message = $"{company.TemplateDisplayName} template selected.";
        return Page();
    }

    private async Task<Company> GetCompanyAsync()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        return await _db.Companies.FirstAsync(c => c.Id == companyId);
    }

    private void Load(Company company)
    {
        CompanyName = string.IsNullOrWhiteSpace(company.Name) ? "Your Company" : company.Name;
        HasLogo = company.LogoBytes is { Length: > 0 };
        InvoiceTemplateKey = company.InvoiceTemplateKey ?? "default_modern";
        TemplateDisplayName = company.TemplateDisplayName ?? GetTemplateDisplayName(InvoiceTemplateKey);
        DefaultCurrency = company.DefaultCurrency ?? "INR";
        PrimaryColor = company.PrimaryColor ?? GetDefaultPrimary(InvoiceTemplateKey);
        TableHeaderColor = company.TableHeaderColor ?? GetDefaultHeader(InvoiceTemplateKey);
        PdfFontSize = company.PdfFontSize <= 0 ? 11 : company.PdfFontSize;
        PdfTitleStyle = company.PdfTitleStyle ?? "Bold";
        ShowHsnSac = company.ShowHsnSac;
        ShowSgst = company.ShowSgst;
        ShowCgst = company.ShowCgst;
        ShowIgst = company.ShowIgst;
        ShowCess = company.ShowCess;
        ShowTerms = company.ShowTerms;
        ShowNotes = company.ShowNotes;
        ShowPaymentDetails = company.ShowPaymentDetails;
        ShowSignature = company.ShowSignature;
        SignatureLabel = string.IsNullOrWhiteSpace(company.SignatureLabel) ? "Authorized Signature" : company.SignatureLabel;
        SignatureName = company.SignatureName;
        PaymentDetails = company.PaymentDetails;
        CustomColumn1Name = company.CustomColumn1Name ?? "Item";
        CustomColumn2Name = company.CustomColumn2Name ?? "Qty";
        CustomColumn3Name = company.CustomColumn3Name ?? "Unit Price";
        CustomColumn4Name = company.CustomColumn4Name ?? "Total";
        TaxAuto = company.TaxAuto;
        TaxEnabled = company.TaxEnabled;
        TaxPresetKey = company.TaxPresetKey ?? "kuwait_vat";
        TaxLabel = company.TaxLabel ?? "Tax";
        TaxRate = company.TaxRate;
        Country = company.Country;
    }

    private static string NormalizeColumn(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeTemplateKey(string? key)
        => string.IsNullOrWhiteSpace(key) ? "default_modern" : key.Trim().ToLowerInvariant();

    private static string NormalizeCurrency(string? value)
    {
        var currency = string.IsNullOrWhiteSpace(value) ? "INR" : value.Trim().ToUpperInvariant();
        return currency is "INR" or "KWD" or "USD" ? currency : "INR";
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var text = value.Trim();
        if (!text.StartsWith("#")) text = "#" + text;
        return text.Length == 7 ? text : fallback;
    }

    private static string GetTemplateDisplayName(string key) => key switch
    {
        "clean_minimal" => "Clean Minimal",
        "gst_pro" => "GST Pro",
        "premium_classic" => "Premium Classic",
        "corporate_violet" => "Corporate Violet",
        "alloy_farmer" => "Allowyfarmer",
        _ => "Default Modern"
    };

    private static string GetDefaultPrimary(string key) => key switch
    {
        "clean_minimal" => "#4b5563",
        "gst_pro" => "#15803d",
        "premium_classic" => "#a16207",
        "corporate_violet" => "#6d28d9",
        "alloy_farmer" => "#111827",
        _ => "#2563eb"
    };

    private static string GetDefaultHeader(string key) => key switch
    {
        "clean_minimal" => "#9ca3af",
        "gst_pro" => "#166534",
        "premium_classic" => "#1f2937",
        "corporate_violet" => "#6d28d9",
        "alloy_farmer" => "#111827",
        _ => "#1d4ed8"
    };

    private static void ApplyTemplateDefaults(Company company, string key)
    {
        company.InvoiceTemplateKey = key;
        company.TemplateDisplayName = GetTemplateDisplayName(key);
        company.PrimaryColor = GetDefaultPrimary(key);
        company.TableHeaderColor = GetDefaultHeader(key);
        company.ShowTerms = true;
        company.ShowNotes = true;
        company.ShowSignature = key is "premium_classic" or "corporate_violet";
        company.SignatureLabel = string.IsNullOrWhiteSpace(company.SignatureLabel) ? "Authorized Signature" : company.SignatureLabel;

        switch (key)
        {
            case "gst_pro":
                company.PdfTitleStyle = "Bold";
                company.DefaultCurrency = "INR";
                company.StripeCurrency = "inr";
                company.Country = "India";
                company.TaxAuto = true;
                company.TaxPresetKey = "india_gst";
                break;
            case "corporate_violet":
                company.PdfTitleStyle = "Bold";
                break;
            case "premium_classic":
                company.PdfTitleStyle = "Premium";
                break;
            case "clean_minimal":
                company.PdfTitleStyle = "Simple";
                break;
            default:
                company.PdfTitleStyle = "Bold";
                break;
        }
    }

    private static void ApplyPreset(Company company)
    {
        var country = (company.Country ?? string.Empty).Trim().ToLowerInvariant();
        var currency = (company.DefaultCurrency ?? string.Empty).Trim().ToUpperInvariant();
        var preset = (company.TaxPresetKey ?? string.Empty).Trim().ToLowerInvariant();

        if (company.TaxAuto)
        {
            if (currency == "INR" || country.Contains("india") || preset == "india_gst")
            {
                company.Country = "India";
                company.DefaultCurrency = "INR";
                company.StripeCurrency = "inr";
                company.TaxPresetKey = "india_gst";
                company.TaxEnabled = true;
                company.TaxLabel = "GST";
                if (company.TaxRate <= 0) company.TaxRate = 18m;
                company.ShowHsnSac = true;
                company.ShowSgst = true;
                company.ShowCgst = true;
                company.ShowIgst = false;
                company.ShowCess = false;
            }
            else if (currency == "KWD" || country.Contains("kuwait") || preset == "kuwait_vat")
            {
                company.Country = "Kuwait";
                company.DefaultCurrency = "KWD";
                company.StripeCurrency = "kwd";
                company.TaxPresetKey = "kuwait_vat";
                company.TaxEnabled = false;
                company.TaxLabel = "VAT";
                company.TaxRate = 0m;
                company.ShowHsnSac = false;
                company.ShowSgst = false;
                company.ShowCgst = false;
                company.ShowIgst = false;
                company.ShowCess = false;
            }
            else if (currency == "USD" || country.Contains("usa") || country.Contains("united states") || country == "us" || country.Contains("america") || preset == "us_sales")
            {
                company.Country = "USA";
                company.DefaultCurrency = "USD";
                company.StripeCurrency = "usd";
                company.TaxPresetKey = "us_sales";
                company.TaxEnabled = true;
                company.TaxLabel = "Sales Tax";
                if (company.TaxRate <= 0) company.TaxRate = 8m;
                company.ShowHsnSac = false;
                company.ShowSgst = false;
                company.ShowCgst = false;
                company.ShowIgst = false;
                company.ShowCess = false;
            }
        }

        if (!company.TaxEnabled)
        {
            company.TaxRate = 0m;
            company.ShowSgst = false;
            company.ShowCgst = false;
            company.ShowIgst = false;
            company.ShowCess = false;
        }
    }
}
