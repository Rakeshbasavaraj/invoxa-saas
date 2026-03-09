using Invoxa.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoxa.Web.Services;

namespace Invoxa.Web.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public bool IsAuthenticated { get; set; }
    public string CompanyName { get; set; } = "Invoxa";
    public string? Message { get; set; }

    // KPI
    public decimal TotalRevenue { get; set; }
    public decimal ThisMonthRevenue { get; set; }
    public decimal PendingAmount { get; set; }
    public int OverdueCount { get; set; }
    public decimal OverdueAmount { get; set; }
    public int ClientCount { get; set; }

    // UI selection
    public int Days { get; set; } = 14;

    public List<RecentInvoiceVm> RecentInvoices { get; set; } = new();

    // Charts
    public List<string> RevenueLabels { get; set; } = new();
    public List<decimal> RevenueValues { get; set; } = new();
    public Dictionary<string, int> StatusCounts { get; set; } = new();

    public List<string> TopClientLabels { get; set; } = new();
    public List<decimal> TopClientValues { get; set; } = new();

    public async Task OnGet(int? days, string? message = null)
    {
        Message = message;
        Days = (days is >= 7 and <= 60) ? days!.Value : 14;
        IsAuthenticated = CurrentUserContext.IsAuthenticated(HttpContext);
        if (!IsAuthenticated)
        {
            CompanyName = "Invoxa";
            return;
        }

        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        CompanyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync() ?? "Invoxa";
        ClientCount = await _db.Clients.CountAsync(c => c.CompanyId == companyId);

        var invoices = await _db.Invoices
            .Include(i => i.Client)
            .Include(i => i.Items)
            .Where(i => i.CompanyId == companyId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync();

        // KPI
        TotalRevenue = invoices.Where(i => i.Status == Domain.InvoiceStatus.Paid).Sum(i => i.Total);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        ThisMonthRevenue = invoices
            .Where(i => i.Status == Domain.InvoiceStatus.Paid && i.IssueDate >= monthStart && i.IssueDate <= today)
            .Sum(i => i.Total);

        PendingAmount = invoices
            .Where(i => i.Status == Domain.InvoiceStatus.Unpaid || i.Status == Domain.InvoiceStatus.Overdue)
            .Sum(i => i.Total);

        OverdueCount = invoices.Count(i => i.Status != Domain.InvoiceStatus.Paid && i.DueDate < today);
        OverdueAmount = invoices
            .Where(i => i.Status != Domain.InvoiceStatus.Paid && i.DueDate < today)
            .Sum(i => i.Total);

        // Recent invoices
        RecentInvoices = invoices.Take(8).Select(i => new RecentInvoiceVm
        {
            Id = i.Id,
            InvoiceNumber = i.InvoiceNumber,
            ClientName = i.Client!.Name,
            DueDate = i.DueDate.ToString("yyyy-MM-dd"),
            Status = i.Status.ToString(),
            Total = i.Total
        }).ToList();

        // Chart 1: Paid revenue by day (last N days)
        var start = today.AddDays(-(Days - 1));
        for (var d = start; d <= today; d = d.AddDays(1))
        {
            RevenueLabels.Add(d.ToString("MMM dd"));
            var dayRevenue = invoices
                .Where(i => i.Status == Domain.InvoiceStatus.Paid && i.IssueDate == d)
                .Sum(i => i.Total);
            RevenueValues.Add(dayRevenue);
        }

        StatusCounts = invoices
            .GroupBy(i => i.Status)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());

        var top = invoices
            .Where(i => i.Status == Domain.InvoiceStatus.Paid && i.Client != null)
            .GroupBy(i => i.Client!.Name)
            .Select(g => new { Client = g.Key, Amount = g.Sum(x => x.Total) })
            .OrderByDescending(x => x.Amount)
            .Take(5)
            .ToList();

        TopClientLabels = top.Select(x => x.Client).ToList();
        TopClientValues = top.Select(x => x.Amount).ToList();
    }

    public class RecentInvoiceVm
    {
        public Guid Id { get; set; }
        public string InvoiceNumber { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string DueDate { get; set; } = "";
        public string Status { get; set; } = "";
        public decimal Total { get; set; }
    }
}
