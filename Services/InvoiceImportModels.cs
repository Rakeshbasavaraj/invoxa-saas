using System.Text.Json.Serialization;

namespace Invoxa.Web.Services;

public class ExtractedInvoiceDto
{
    public string? ClientName { get; set; }
    public string? ClientEmail { get; set; }
    public string? ClientPhone { get; set; }

    public string? InvoiceNumber { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Notes { get; set; }

    public List<ExtractedItemDto> Items { get; set; } = new();
}

public class ExtractedItemDto
{
    public string? Description { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; } = 0m;
}
