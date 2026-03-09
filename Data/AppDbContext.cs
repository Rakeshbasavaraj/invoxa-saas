using Invoxa.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Company>().HasIndex(x => x.Name);
        modelBuilder.Entity<Company>().HasIndex(x => x.ApprovalStatus);
        modelBuilder.Entity<Company>().HasIndex(x => x.PlanKey);
        modelBuilder.Entity<Client>().HasIndex(x => new { x.CompanyId, x.Email });
        modelBuilder.Entity<Client>().HasIndex(x => x.PortalToken);
        modelBuilder.Entity<Invoice>().HasIndex(x => new { x.CompanyId, x.Status, x.DueDate });
        modelBuilder.Entity<ReminderLog>().HasIndex(x => new { x.CompanyId, x.SentAtUtc });
        modelBuilder.Entity<UserAccount>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<UserAccount>().HasIndex(x => new { x.CompanyId, x.Role });
        modelBuilder.Entity<Client>().HasIndex(x => new { x.CompanyId, x.Email }).IsUnique();
        modelBuilder.Entity<EmailTemplate>().HasIndex(x => new { x.CompanyId, x.TemplateKey }).IsUnique();

        modelBuilder.Entity<InvoiceItem>()
            .HasOne(i => i.Invoice)
            .WithMany(i => i.Items)
            .HasForeignKey(i => i.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
