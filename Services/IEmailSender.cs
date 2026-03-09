namespace Invoxa.Web.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, byte[]? attachment = null, string? attachmentName = null);
}
