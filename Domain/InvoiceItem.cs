namespace Invoxa.Web.Domain;

public class InvoiceItem : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; } = 0m;
    public string? HsnSac { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
