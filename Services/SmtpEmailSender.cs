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
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        AppDbContext db,
        IConfiguration config,
        ILogger<SmtpEmailSender> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string body,
        byte[]? attachment = null,
        string? attachmentName = null)
    {
        var company = await _db.Companies
            .OrderBy(c => c.CreatedAtUtc)
            .FirstAsync();

        // If SMTP is not configured, try SendGrid
        if (string.IsNullOrWhiteSpace(company.SmtpHost) || (company.SmtpPort ?? 0) <= 0)
        {
            _logger.LogInformation("SMTP not configured. Falling back to SendGrid.");
            var sendGrid = new SendGridEmailSender(_config);
            await sendGrid.SendAsync(to, subject, body, attachment, attachmentName);
            return;
        }

        var fromAddress = company.EmailFromAddress;
        if (string.IsNullOrWhiteSpace(fromAddress))
            fromAddress = company.SmtpUsername;

        if (string.IsNullOrWhiteSpace(fromAddress))
            throw new InvalidOperationException(
                "Email not configured. Set Email From Address and SMTP settings in Company Settings.");

        if (string.IsNullOrWhiteSpace(company.SmtpUsername))
            throw new InvalidOperationException("SMTP username is missing.");

        if (string.IsNullOrWhiteSpace(company.SmtpPassword))
            throw new InvalidOperationException("SMTP password / app password is missing.");

        var fromName = string.IsNullOrWhiteSpace(company.EmailFromName)
            ? "Invoxa"
            : company.EmailFromName;

        using var msg = new MailMessage();
        msg.From = new MailAddress(fromAddress, fromName);
        msg.To.Add(new MailAddress(to));
        msg.Subject = subject;
        msg.Body = body;
        msg.IsBodyHtml =
            body.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("<body", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("<a ", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("<table", StringComparison.OrdinalIgnoreCase);

        if (attachment is not null && attachment.Length > 0)
        {
            var stream = new MemoryStream(attachment);
            msg.Attachments.Add(
                new Attachment(
                    stream,
                    attachmentName ?? "invoice.pdf",
                    "application/pdf"));
        }

        using var client = new SmtpClient(company.SmtpHost!, company.SmtpPort!.Value);
        client.EnableSsl = company.SmtpUseSsl;
        client.Timeout = 30000;
        client.DeliveryMethod = SmtpDeliveryMethod.Network;
        client.UseDefaultCredentials = false;
        client.Credentials = new NetworkCredential(
            company.SmtpUsername,
            company.SmtpPassword);

        _logger.LogInformation(
            "SMTP send starting. Host={Host}, Port={Port}, SSL={Ssl}, User={User}, To={To}",
            company.SmtpHost,
            company.SmtpPort,
            company.SmtpUseSsl,
            company.SmtpUsername,
            to);

        try
        {
            await client.SendMailAsync(msg);
            _logger.LogInformation("SMTP send success to {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP send failed to {To}", to);
            throw;
        }
    }
}