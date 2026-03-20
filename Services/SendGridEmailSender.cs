using SendGrid;
using SendGrid.Helpers.Mail;

namespace Invoxa.Web.Services;

public class SendGridEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    public SendGridEmailSender(IConfiguration config) => _config = config;

    public async Task SendAsync(string to, string subject, string body, byte[]? attachment = null, string? attachmentName = null, Guid? companyId = null)
    {
        var apiKey = _config["SendGrid:ApiKey"];
        var fromEmail = _config["SendGrid:FromEmail"];
        var fromName = _config["SendGrid:FromName"] ?? "Invoxa";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(fromEmail))
            throw new InvalidOperationException("SendGrid not configured. Set SendGrid:ApiKey and SendGrid:FromEmail in appsettings.json or user-secrets.");

        var client = new SendGridClient(apiKey);

        var isHtml = body.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<a ", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<table", StringComparison.OrdinalIgnoreCase);

        var msg = new SendGridMessage
        {
            From = new EmailAddress(fromEmail, fromName),
            Subject = subject,
            PlainTextContent = body.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n"),
            HtmlContent = isHtml
                ? body
                : $"<pre style='font-family:ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace'>{System.Net.WebUtility.HtmlEncode(body)}</pre>"
        };

        msg.AddTo(new EmailAddress(to));

        if (attachment is not null && attachment.Length > 0)
        {
            msg.AddAttachment(attachmentName ?? "invoice.pdf", Convert.ToBase64String(attachment), "application/pdf");
        }

        var response = await client.SendEmailAsync(msg);
        if ((int)response.StatusCode >= 400)
        {
            var txt = await response.Body.ReadAsStringAsync();
            throw new Exception($"SendGrid error: {response.StatusCode} {txt}");
        }
    }
}
