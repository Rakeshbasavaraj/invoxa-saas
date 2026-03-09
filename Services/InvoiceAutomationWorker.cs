using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Invoxa.Web.Services;

/// <summary>
/// Simple background automation:
/// - Sends payment reminders automatically
/// - Generates recurring invoices from templates
///
/// This is an MVP worker intended for single-instance / single-tenant demo usage.
/// </summary>
public class InvoiceAutomationWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private readonly ILogger<InvoiceAutomationWorker> _log;

    public InvoiceAutomationWorker(IServiceProvider sp, IConfiguration cfg, ILogger<InvoiceAutomationWorker> log)
    {
        _sp = sp;
        _cfg = cfg;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _log.LogInformation("Invoice automation worker starting");
    await RunOnce(stoppingToken);

    while (!stoppingToken.IsCancellationRequested)
    {
        var delay = await GetCurrentDelayAsync(stoppingToken);
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        _log.LogInformation("Invoice automation worker tick: interval {Delay}", delay);
        await RunOnce(stoppingToken);
    }
}


private async Task<TimeSpan> GetCurrentDelayAsync(CancellationToken ct)
{
    try
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var company = await db.Companies.OrderBy(c => c.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (company == null) return TimeSpan.FromMinutes(1);

        var value = company.AutomationIntervalValue <= 0 ? 1 : company.AutomationIntervalValue;
        var unit = string.IsNullOrWhiteSpace(company.AutomationIntervalUnit) ? "Minutes" : company.AutomationIntervalUnit;

        return string.Equals(unit, "Hours", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromHours(value)
            : TimeSpan.FromMinutes(value);
    }
    catch
    {
        return TimeSpan.FromMinutes(1);
    }
}


    public Task RunNow(CancellationToken ct = default) => RunOnce(ct);

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            await using var scope = _sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pdf = scope.ServiceProvider.GetRequiredService<IPdfGenerator>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var wa = scope.ServiceProvider.GetRequiredService<IWhatsAppSender>();
            var pay = scope.ServiceProvider.GetRequiredService<IPaymentLinkService>();

            var companies = await db.Companies.OrderBy(c => c.CreatedAtUtc).ToListAsync(ct);
            foreach (var company in companies)
            {
                await RunRecurringTemplates(db, company, ct);
                await RunAutoReminders(db, company, pdf, email, wa, pay, ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Invoice automation worker failed");
        }
    }

    private static DateOnly TodayUtc() => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task RunAutoReminders(AppDbContext db, Company company, IPdfGenerator pdf, IEmailSender email, IWhatsAppSender wa, IPaymentLinkService pay, CancellationToken ct)
    {
        var today = TodayUtc();
        var daysBefore = company.ReminderDaysBeforeDue <= 0 ? 2 : company.ReminderDaysBeforeDue;

        var targets = await db.Invoices
            .Include(i => i.Client)
            .Include(i => i.Items)
            .Where(i => i.CompanyId == company.Id)
            .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue)
            .OrderBy(i => i.DueDate)
            .ToListAsync(ct);

        foreach (var inv in targets)
        {
            // Mark overdue automatically
            if (inv.Status != InvoiceStatus.Paid && inv.DueDate < today)
                inv.Status = InvoiceStatus.Overdue;

            var type = GetReminderType(inv, today, daysBefore);
            if (type == null) continue;

            if (await HasAlreadySentForScheduleAsync(db, inv, type, today, company, ct))
                continue;

            try
            {
                await SendReminder(inv, company, type, pdf, email, wa, pay, db, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Automation reminder failed for invoice {InvoiceNumber}", inv.InvoiceNumber);
                db.ReminderLogs.Add(new ReminderLog
                {
                    CompanyId = company.Id,
                    InvoiceId = inv.Id,
                    Actor = "System",
                    Channel = "System",
                    Type = type + "Failed",
                    To = inv.InvoiceNumber,
                    Notes = ex.Message
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? GetReminderType(Invoice inv, DateOnly today, int daysBefore)
    {
        var daysUntilDue = inv.DueDate.DayNumber - today.DayNumber;
        var targetPreDueDays = Math.Max(1, daysBefore);

        // Zoho-style schedule:
        // - exactly N days before due date
        // - on due date
        // - once per day after overdue until paid
        if (daysUntilDue == targetPreDueDays) return "AutoPreDue";
        if (daysUntilDue == 0) return "AutoDue";
        if (daysUntilDue < 0) return "AutoOverdue";
        return null;
    }

    private static async Task<bool> HasAlreadySentForScheduleAsync(AppDbContext db, Invoice inv, string type, DateOnly today, Company company, CancellationToken ct)
    {
        if (type == "AutoOverdue")
        {
            if (!company.OverdueReminderEnabled) return true;

            var latest = await db.ReminderLogs
                .Where(r => r.InvoiceId == inv.Id && (r.Type == type + "Email" || r.Type == type + "WhatsApp"))
                .OrderByDescending(r => r.SentAtUtc)
                .FirstOrDefaultAsync(ct);

            if (latest == null) return false;

            var value = company.OverdueReminderIntervalValue <= 0 ? 1 : company.OverdueReminderIntervalValue;
            var unit = string.IsNullOrWhiteSpace(company.OverdueReminderIntervalUnit) ? "Days" : company.OverdueReminderIntervalUnit;
            var nextAllowed = unit.Equals("Minutes", StringComparison.OrdinalIgnoreCase)
                ? latest.SentAtUtc.AddMinutes(value)
                : unit.Equals("Hours", StringComparison.OrdinalIgnoreCase)
                    ? latest.SentAtUtc.AddHours(value)
                    : latest.SentAtUtc.AddDays(value);
            return DateTime.UtcNow < nextAllowed;
        }

        return await db.ReminderLogs.AnyAsync(
            r => r.InvoiceId == inv.Id && (r.Type == type + "Email" || r.Type == type + "WhatsApp"),
            ct);
    }

    private async Task SendReminder(Invoice inv, Company company, string type, IPdfGenerator pdf, IEmailSender email, IWhatsAppSender wa, IPaymentLinkService pay, AppDbContext db, CancellationToken ct)
    {
        var dueTxt = inv.DueDate.ToString("yyyy-MM-dd");
        var baseUrl = _cfg["App:BaseUrl"]?.TrimEnd('/') ?? "http://localhost:5000";
        var viewUrl = $"{baseUrl}/i/{inv.PublicToken}";
        if (string.IsNullOrWhiteSpace(inv.PaymentLink) && inv.Total > 0)
        {
            var paymentUrl = await pay.CreatePaymentLinkAsync(inv, company, ct);
            if (!string.IsNullOrWhiteSpace(paymentUrl))
            {
                inv.PaymentLink = paymentUrl;
                inv.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        var payUrl = string.IsNullOrWhiteSpace(inv.PaymentLink) ? viewUrl : inv.PaymentLink!;
        var title = type switch
        {
            "AutoPreDue" => "Invoice due soon",
            "AutoDue" => "Payment due today",
            _ => "Invoice overdue"
        };
        var subtitle = type switch
        {
            "AutoPreDue" => $"Invoice {inv.InvoiceNumber} is due in {Math.Max(0, (inv.DueDate.DayNumber - TodayUtc().DayNumber))} day(s)",
            "AutoDue" => $"Invoice {inv.InvoiceNumber} is due today",
            _ => $"Invoice {inv.InvoiceNumber} is overdue — please pay now"
        };
        var intro = type switch
        {
            "AutoPreDue" => $"This is a reminder that invoice {inv.InvoiceNumber} is due on {dueTxt}. Please make payment by the due date.",
            "AutoDue" => $"This is your due-date reminder for invoice {inv.InvoiceNumber}. Please complete payment today.",
            _ => $"Invoice {inv.InvoiceNumber} is past due since {dueTxt}. Please complete payment as soon as possible."
        };
        var overdueDays = Math.Max(0, TodayUtc().DayNumber - inv.DueDate.DayNumber);
        var subject = type switch
        {
            "AutoPreDue" => $"Reminder: {inv.InvoiceNumber} due on {dueTxt}",
            "AutoDue" => $"Due today: {inv.InvoiceNumber}",
            _ => $"Overdue ({overdueDays} day{(overdueDays == 1 ? "" : "s")}): {inv.InvoiceNumber}"
        };

        string BuildReminderHtml()
        {
            return $@"<html>
<body style='margin:0;padding:0;background:#f5f7fb;font-family:Arial,Helvetica,sans-serif;color:#0f172a;'>
  <div style='max-width:640px;margin:24px auto;background:#ffffff;border:1px solid #e5e7eb;border-radius:16px;overflow:hidden;'>
    <div style='padding:24px 28px;background:linear-gradient(135deg,#2563eb,#1d4ed8);color:#ffffff;'>
      <div style='font-size:28px;font-weight:800;'>{company.Name}</div>
      <div style='margin-top:6px;font-size:14px;opacity:0.92;'>{subtitle}</div>
    </div>
    <div style='padding:28px;'>
      <p style='margin:0 0 18px 0;font-size:16px;'>Hello {inv.Client?.Name},</p>
      <p style='margin:0 0 16px 0;font-size:15px;line-height:1.7;'>{intro}</p>
      <div style='background:#f8fafc;border:1px solid #e2e8f0;border-radius:12px;padding:16px;margin:18px 0;'>
        <div style='font-size:14px;color:#64748b;'>Amount Due</div>
        <div style='font-size:28px;font-weight:800;margin-top:6px;'>{inv.Total:0.00}</div>
        <div style='font-size:14px;color:#64748b;margin-top:8px;'>Due Date: {dueTxt}</div>
      </div>
      <div style='margin:24px 0 12px 0;'>
        <a href='{payUrl}' style='display:inline-block;background:#16a34a;color:#ffffff;text-decoration:none;padding:14px 22px;border-radius:10px;font-weight:700;margin-right:12px;'>Pay Now</a>
        <a href='{viewUrl}' style='display:inline-block;background:#eff6ff;color:#1d4ed8;text-decoration:none;padding:14px 22px;border-radius:10px;font-weight:700;border:1px solid #bfdbfe;'>View Invoice</a>
      </div>
      <p style='margin:18px 0 0 0;font-size:14px;color:#64748b;line-height:1.7;'>A PDF copy of the invoice is attached for your records.</p>
      <p style='margin:20px 0 0 0;font-size:14px;color:#64748b;line-height:1.7;'>Thanks,<br>{company.Name}</p>
    </div>
  </div>
</body>
</html>";
        }

        // Email
        var toEmail = inv.Client?.Email;
        if (!string.IsNullOrWhiteSpace(toEmail))
        {
            var bytes = pdf.GenerateInvoicePdf(inv, company);
            var html = BuildReminderHtml();
            await email.SendAsync(toEmail, subject, html, bytes, $"{inv.InvoiceNumber}.pdf");

            db.ReminderLogs.Add(new ReminderLog
            {
                CompanyId = company.Id,
                InvoiceId = inv.Id,
                Actor = "System",
                Channel = "Email",
                Type = type + "Email",
                To = toEmail,
                Notes = subject
            });
        }

        // WhatsApp (best-effort)
        var toPhone = inv.Client?.Phone;
        if (!string.IsNullOrWhiteSpace(toPhone))
        {
            var msg = type switch
            {
                "AutoPreDue" => $"Hi {inv.Client!.Name}, reminder: invoice {inv.InvoiceNumber} for {inv.Total:0.00} is due on {dueTxt}. Please pay by the due date. Pay now: {payUrl} View invoice: {viewUrl}",
                "AutoDue" => $"Hi {inv.Client!.Name}, invoice {inv.InvoiceNumber} for {inv.Total:0.00} is due today. Pay now: {payUrl} View invoice: {viewUrl}",
                _ => $"Hi {inv.Client!.Name}, invoice {inv.InvoiceNumber} for {inv.Total:0.00} is overdue since {dueTxt}. Please pay now: {payUrl} View invoice: {viewUrl}"
            };
            try
            {
                await wa.SendAsync(NormalizePhone(toPhone), msg);
                db.ReminderLogs.Add(new ReminderLog
                {
                    CompanyId = company.Id,
                    InvoiceId = inv.Id,
                    Actor = "System",
                    Channel = "WhatsApp",
                    Type = type + "WhatsApp",
                    To = toPhone,
                    Notes = msg
                });
            }
            catch
            {
                // ignore in automation
            }
        }
    }

    private async Task RunRecurringTemplates(AppDbContext db, Company company, CancellationToken ct)
    {
        var today = TodayUtc();

        var templates = await db.Invoices
            .Include(i => i.Items)
            .Where(i => i.CompanyId == company.Id)
            .Where(i => i.IsRecurringTemplate && i.RecurrenceEnabled)
            .Where(i => i.NextOccurrenceDate != null && i.NextOccurrenceDate <= today)
            .OrderBy(i => i.NextOccurrenceDate)
            .ToListAsync(ct);

        if (templates.Count == 0) return;

        foreach (var t in templates)
        {
            // generate invoice number
            var prefix = string.IsNullOrWhiteSpace(company.InvoicePrefix) ? "INV" : company.InvoicePrefix.Trim().ToUpperInvariant();
            if (company.NextInvoiceNumber <= 0) company.NextInvoiceNumber = 1;
            var newNo = $"{prefix}-{company.NextInvoiceNumber:0000}";
            company.NextInvoiceNumber++;
            company.UpdatedAtUtc = DateTime.UtcNow;

            var dueDays = Math.Max(0, (t.DueDate.ToDateTime(TimeOnly.MinValue) - t.IssueDate.ToDateTime(TimeOnly.MinValue)).Days);
            var issue = today;
            var due = today.AddDays(dueDays);

            var inv = new Invoice
            {
                CompanyId = company.Id,
                ClientId = t.ClientId,
                InvoiceNumber = newNo,
                PublicToken = Guid.NewGuid().ToString("N"),
                IssueDate = issue,
                DueDate = due,
                Status = InvoiceStatus.Unpaid,
                Notes = t.Notes,
                ShipToName = t.ShipToName,
                ShipToAddressLine1 = t.ShipToAddressLine1,
                ShipToAddressLine2 = t.ShipToAddressLine2,
                ShipToCity = t.ShipToCity,
                ShipToCountry = t.ShipToCountry,
                RecurrenceEnabled = false,
                IsRecurringTemplate = false,
                CreatedAtUtc = DateTime.UtcNow
            };

            foreach (var it in t.Items)
            {
                inv.Items.Add(new InvoiceItem { Description = it.Description, Quantity = it.Quantity, UnitPrice = it.UnitPrice });
            }

            db.Invoices.Add(inv);
            db.ReminderLogs.Add(new ReminderLog
            {
                CompanyId = company.Id,
                InvoiceId = inv.Id,
                Actor = "System",
                Channel = "System",
                Type = "RecurringGenerated",
                To = newNo,
                Notes = $"Generated from template {t.InvoiceNumber}"
            });

            // schedule next
            var interval = Math.Max(1, t.RecurrenceIntervalDays);
            t.NextOccurrenceDate = today.AddDays(interval);
            t.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private static string NormalizePhone(string phone)
    {
        var p = phone.Trim();
        if (p.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase))
            p = p.Substring("whatsapp:".Length);
        if (!p.StartsWith("+") && p.All(char.IsDigit))
            p = "+" + p;
        return p;
    }
}
