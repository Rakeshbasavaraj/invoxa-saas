namespace Invoxa.Web.Domain;

public class Company : BaseEntity
{
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? VatNumber { get; set; }

    public string? ThankYouNote { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? PaymentDetails { get; set; }

    public bool TaxAuto { get; set; } = true;
    public bool TaxEnabled { get; set; } = false;
    public string? TaxPresetKey { get; set; } = "kuwait_vat";
    public string? TaxLabel { get; set; } = "Tax";
    public decimal TaxRate { get; set; } = 0m;

    public string? DefaultCurrency { get; set; } = "INR";
    public string? InvoiceTemplateKey { get; set; } = "default_modern";
    public string? TemplateDisplayName { get; set; } = "Default Modern";
    public string? PrimaryColor { get; set; } = "#2563eb";
    public string? TableHeaderColor { get; set; } = "#1d4ed8";
    public int PdfFontSize { get; set; } = 11;
    public string? PdfTitleStyle { get; set; } = "Bold";

    public bool ShowHsnSac { get; set; }
    public bool ShowSgst { get; set; }
    public bool ShowCgst { get; set; }
    public bool ShowIgst { get; set; }
    public bool ShowCess { get; set; }
    public bool ShowTerms { get; set; } = true;
    public bool ShowNotes { get; set; } = true;
    public bool ShowPaymentDetails { get; set; }
    public bool ShowSignature { get; set; }
    public string? SignatureLabel { get; set; } = "Authorized Signature";
    public string? SignatureName { get; set; }

    public string? CustomColumn1Name { get; set; } = "Item";
    public string? CustomColumn2Name { get; set; } = "Qty";
    public string? CustomColumn3Name { get; set; } = "Unit Price";
    public string? CustomColumn4Name { get; set; } = "Total";

    public string ApprovalStatus { get; set; } = "Active";
    public DateTime? ApprovedAtUtc { get; set; }
    public string PlanKey { get; set; } = "Free";
    public int InvoiceLimit { get; set; } = 10;
    public int ClientLimit { get; set; } = 5;

    public string InvoicePrefix { get; set; } = "INV";
    public int NextInvoiceNumber { get; set; } = 1;

    public byte[]? LogoBytes { get; set; }
    public string? LogoContentType { get; set; }
    public int? LogoWidth { get; set; } = 160;
    public int? LogoHeight { get; set; }
    public string? LogoFitMode { get; set; } = "contain";

    public string? EmailFromName { get; set; }
    public string? EmailFromAddress { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;

    public string? StripePublishableKey { get; set; }
    public string? StripeSecretKey { get; set; }
    public string? StripeCurrency { get; set; } = "usd";

    public int ReminderDaysBeforeDue { get; set; } = 2;
    public int AutomationIntervalValue { get; set; } = 1;
    public string AutomationIntervalUnit { get; set; } = "Minutes";

    public bool OverdueReminderEnabled { get; set; } = true;
    public int OverdueReminderIntervalValue { get; set; } = 1;
    public string OverdueReminderIntervalUnit { get; set; } = "Days";

    // Allowyfarmer fixed industrial template settings
    public string? AllowyFarmerCompanyName { get; set; }
    public string? AllowyFarmerAddressLine1 { get; set; }
    public string? AllowyFarmerAddressLine2 { get; set; }
    public string? AllowyFarmerCityLine { get; set; }
    public string? AllowyFarmerWebsite { get; set; }
    public string? AllowyFarmerPhone { get; set; }
    public string? AllowyFarmerGstNumber { get; set; }

    public string? AllowyFarmerInvoicePrefix { get; set; }
    public string? AllowyFarmerDefaultDeliveryNote { get; set; }
    public string? AllowyFarmerDefaultModeTerms { get; set; }
    public string? AllowyFarmerDefaultHsnCode { get; set; }

    public string? AllowyFarmerBankBeneficiary { get; set; }
    public string? AllowyFarmerBankAccountNumber { get; set; }
    public string? AllowyFarmerBankAccountType { get; set; }
    public string? AllowyFarmerBankName { get; set; }
    public string? AllowyFarmerBankBranch { get; set; }
    public string? AllowyFarmerBankIfscCode { get; set; }
    public string? AllowyFarmerFooterNote { get; set; }

}
