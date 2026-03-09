namespace Invoxa.Web.Domain;

public class Company : BaseEntity
{
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = "UTC";

    // Address / business details (optional)
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? VatNumber { get; set; }

    // PDF footer text (editable)
    public string? ThankYouNote { get; set; }
    public string? TermsAndConditions { get; set; }


    // Tax settings (optional)
    public bool TaxAuto { get; set; } = true; // auto-apply based on Country
    // Presets: india_gst, kuwait_vat, us_sales
    public bool TaxEnabled { get; set; } = false;
    public string? TaxPresetKey { get; set; } = "kuwait_vat";
    public string? TaxLabel { get; set; } = "Tax";
    public decimal TaxRate { get; set; } = 0m;

    // PDF template + branding (optional)
    // Example keys: classic, modern, minimal, professional_blue
    public string? InvoiceTemplateKey { get; set; } = "classic";



    // SaaS plan + approval
    public string ApprovalStatus { get; set; } = "Active"; // Pending, Active, Suspended
    public DateTime? ApprovedAtUtc { get; set; }
    public string PlanKey { get; set; } = "Free"; // Free, Starter, Pro
    public int InvoiceLimit { get; set; } = 10;
    public int ClientLimit { get; set; } = 5;

    // Invoice numbering
    // Example: Prefix = "INV" -> INV-0001, INV-0002...
    public string InvoicePrefix { get; set; } = "INV";
    public int NextInvoiceNumber { get; set; } = 1;

    // Logo stored in DB (optional)
    public byte[]? LogoBytes { get; set; }
    public string? LogoContentType { get; set; }

    // Email (SMTP) settings (optional)
    public string? EmailFromName { get; set; }
    public string? EmailFromAddress { get; set; }
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;


    // Stripe payment settings (optional, company-level)
    public string? StripePublishableKey { get; set; }
    public string? StripeSecretKey { get; set; }
    public string? StripeCurrency { get; set; } = "usd";

    // Automation settings
    public int ReminderDaysBeforeDue { get; set; } = 2;
    public int AutomationIntervalValue { get; set; } = 1;
    public string AutomationIntervalUnit { get; set; } = "Minutes";

    // Overdue reminder settings
    public bool OverdueReminderEnabled { get; set; } = true;
    public int OverdueReminderIntervalValue { get; set; } = 1;
    public string OverdueReminderIntervalUnit { get; set; } = "Days";
}
