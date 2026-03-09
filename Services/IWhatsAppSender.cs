namespace Invoxa.Web.Services;

public interface IWhatsAppSender
{
    Task SendAsync(string toPhoneE164, string message);
}
