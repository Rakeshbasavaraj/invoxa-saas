namespace Invoxa.Web.Domain;

public abstract class TenantEntity : BaseEntity
{
    public Guid CompanyId { get; set; }
}
