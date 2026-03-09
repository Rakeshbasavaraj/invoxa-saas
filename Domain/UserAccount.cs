namespace Invoxa.Web.Domain;

public class UserAccount : BaseEntity
{
    public Guid CompanyId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Admin";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }

    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiryUtc { get; set; }
}
