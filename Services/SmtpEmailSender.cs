using System.Net;
using System.Net.Mail;
using Invoxa.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Invoxa.Web.Services;

/// <summary>
/// Simple SMTP sender using company settings stored in DB.
/// Falls back to SendGrid if SMTP is not configured.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SmtpEmailSender(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task SendAsync(string to, string subject, string body, byte[]? attachment = null, string? attachmentName = null)
    {
        var company = await _db.Companies.OrderBy(c => c.CreatedAtUtc).FirstAsync();

        // If SMTP is not configured, try SendGrid (existing implementation)
        if (string.IsNullOrWhiteSpace(company.SmtpHost) || (company.SmtpPort ?? 0) <= 0)
        {
            var sendGrid = new SendGridEmailSender(_config);
            await sendGrid.SendAsync(to, subject, body, attachment, attachmentName);
            return;
        }

        var fromAddress = company.EmailFromAddress;
        if (string.IsNullOrWhiteSpace(fromAddress))
            fromAddress = company.SmtpUsername;
        if (string.IsNullOrWhiteSpace(fromAddress))
            throw new InvalidOperationException("Email not configured. Set Email From Address and SMTP settings in Company Settings.");

        var fromName = string.IsNullOrWhiteSpace(company.EmailFromName) ? "Invoxa" : company.EmailFromName;

        using var msg = new MailMessage();
        msg.From = new MailAddress(fromAddress!, fromName);
        msg.To.Add(new MailAddress(to));
        msg.Subject = subject;
        msg.Body = body;
        msg.IsBodyHtml = body.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<a ", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<table", StringComparison.OrdinalIgnoreCase);

        if (attachment is not null && attachment.Length > 0)
        {
            msg.Attachments.Add(new Attachment(new MemoryStream(attachment), attachmentName ?? "invoice.pdf", "application/pdf"));
        }

        using var client = new SmtpClient(company.SmtpHost!, company.SmtpPort!.Value);
        client.EnableSsl = company.SmtpUseSsl;

        if (!string.IsNullOrWhiteSpace(company.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(company.SmtpUsername, company.SmtpPassword);
        }
        else
        {
            client.UseDefaultCredentials = true;
        }

        // SmtpClient is synchronous; wrap to keep API async
        await Task.Run(() => client.Send(msg));
    }
}
