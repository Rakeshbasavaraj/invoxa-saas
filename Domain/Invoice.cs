namespace Invoxa.Web.Domain;

public class Invoice : TenantEntity
{
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public string InvoiceNumber { get; set; } = "";

    // Public share token (for /i/{token} public invoice page)
    public string PublicToken { get; set; } = "";
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    // Payment link (eg. Stripe Checkout URL). Optional.
    public string? PaymentLink { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    // Recurring invoices
    public bool RecurrenceEnabled { get; set; }
    /// <summary>Simple MVP recurrence in days (30 = monthly, 7 = weekly)</summary>
    public int RecurrenceIntervalDays { get; set; } = 30;
    public DateOnly? NextOccurrenceDate { get; set; }
    /// <summary>If true, this invoice acts as a recurring template and will be cloned.</summary>
    public bool IsRecurringTemplate { get; set; }

    public string? Notes { get; set; }

    // Optional Ship To (for delivery / billing different address)
    public string? ShipToName { get; set; }
    public string? ShipToAddressLine1 { get; set; }
    public string? ShipToAddressLine2 { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToCountry { get; set; }

    public List<InvoiceItem> Items { get; set; } = new();

    public decimal Subtotal => Items.Sum(i => i.LineTotal);
    public decimal Total => Subtotal;
}
