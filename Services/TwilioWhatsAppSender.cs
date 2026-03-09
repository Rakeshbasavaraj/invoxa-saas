using System.Net.Http.Headers;

namespace Invoxa.Web.Services;

/// <summary>
/// Minimal Twilio WhatsApp sender (no extra NuGet). Configure:
/// Twilio:AccountSid, Twilio:AuthToken, Twilio:FromWhatsApp (e.g. "whatsapp:+14155238886")
/// </summary>
public class TwilioWhatsAppSender : IWhatsAppSender
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;

    public TwilioWhatsAppSender(IConfiguration config, IHttpClientFactory http)
    {
        _config = config;
        _http = http;
    }

    public async Task SendAsync(string toPhoneE164, string message)
    {
        var sid = _config["Twilio:AccountSid"];
        var token = _config["Twilio:AuthToken"];
        var from = _config["Twilio:FromWhatsApp"]; // must include whatsapp:

        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Twilio WhatsApp not configured. Set Twilio:AccountSid, Twilio:AuthToken, Twilio:FromWhatsApp.");

        var to = toPhoneE164.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase)
            ? toPhoneE164
            : $"whatsapp:{toPhoneE164}";

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        var authBytes = System.Text.Encoding.ASCII.GetBytes($"{sid}:{token}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = from,
            ["To"] = to,
            ["Body"] = message
        });

        var client = _http.CreateClient();
        var resp = await client.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new Exception($"Twilio error: {(int)resp.StatusCode} {body}");
        }
    }
}
