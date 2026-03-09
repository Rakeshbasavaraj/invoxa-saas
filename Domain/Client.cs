namespace Invoxa.Web.Domain;

public class Client : TenantEntity
{
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }

    // Client portal token (for /c/{token} client portal)
    public string PortalToken { get; set; } = "";

    // Address (optional)
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
}
