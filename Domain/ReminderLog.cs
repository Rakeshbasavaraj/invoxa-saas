namespace Invoxa.Web.Domain;

public class ReminderLog : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public string Actor { get; set; } = "Admin";
    public string Channel { get; set; } = "System";
    public string Type { get; set; } = "Manual";
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public string? To { get; set; }
    public string? Notes { get; set; }
}
