namespace Invoxa.Web.Domain;

public class EmailTemplate
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    public string TemplateKey { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
