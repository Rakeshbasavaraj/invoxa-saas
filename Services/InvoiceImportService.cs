using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Invoxa.Web.Services;

public class InvoiceImportService : IInvoiceImportService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public InvoiceImportService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public async Task<ExtractedInvoiceDto> ExtractFromPdfAsync(Stream pdfStream, CancellationToken ct = default)
    {
        var text = ExtractPdfText(pdfStream);

        // If OpenAI key is configured, try AI extraction; otherwise fall back to regex extraction
        var apiKey = _config["OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var ai = await ExtractWithOpenAIAsync(text, apiKey!, ct);
                if (ai.Items.Count > 0) return ai;
            }
            catch
            {
                // ignore; fallback below
            }
        }

        return ExtractWithHeuristics(text);
    }

    private static string ExtractPdfText(Stream pdfStream)
    {
        pdfStream.Position = 0;
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(pdfStream);
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private async Task<ExtractedInvoiceDto> ExtractWithOpenAIAsync(string text, string apiKey, CancellationToken ct)
    {
        // Uses an OpenAI-compatible Chat Completions endpoint.
        // Configure:
        // OpenAI:BaseUrl (optional, default https://api.openai.com)
        // OpenAI:Model (optional, default gpt-4o-mini)
        var baseUrl = _config["OpenAI:BaseUrl"] ?? "https://api.openai.com";
        var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";

        var prompt = """
You are an invoice parser. Extract structured data from the invoice text.
Return ONLY valid JSON with this exact shape:
{
  "clientName": "",
  "clientEmail": "",
  "clientPhone": "",
  "invoiceNumber": "",
  "issueDate": "YYYY-MM-DD",
  "dueDate": "YYYY-MM-DD",
  "notes": "",
  "items": [
    { "description": "", "quantity": 1, "unitPrice": 0.0 }
  ]
}

Rules:
- If a field is unknown, use empty string.
- quantity must be integer.
- unitPrice must be number.
- Use best guess from the text.

Text:
""" + text;

        var body = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = "You extract invoice fields and items as JSON only." },
                new { role = "user", content = prompt }
            },
            temperature = 0
        };

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var resp = await client.PostAsync("/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);

        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "{}";

        // Some models wrap JSON in ```json ... ```; strip if needed
        content = Regex.Replace(content, @"^```json\s*|```$", "", RegexOptions.Multiline).Trim();

        var dto = JsonSerializer.Deserialize<ExtractedInvoiceDto>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new ExtractedInvoiceDto();

        return Normalize(dto);
    }

    private static ExtractedInvoiceDto ExtractWithHeuristics(string text)
    {
        var dto = new ExtractedInvoiceDto();

        // Invoice number common patterns
        var invNo = Regex.Match(text, @"(INV[-\s]?[A-Z0-9]{3,}|Invoice\s*(Number|No\.?|#)\s*[:\-]?\s*([A-Z0-9\-\/]+))", RegexOptions.IgnoreCase);
        if (invNo.Success)
        {
            dto.InvoiceNumber = invNo.Groups.Count >= 4 && !string.IsNullOrWhiteSpace(invNo.Groups[3].Value)
                ? invNo.Groups[3].Value.Trim()
                : invNo.Value.Trim();
        }

        // Emails
        var email = Regex.Match(text, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
        if (email.Success) dto.ClientEmail = email.Value;

        // Dates (best effort)
        DateTime.TryParse(Regex.Match(text, @"(Issue\s*Date|Date)\s*[:\-]?\s*([0-9]{1,2}[\/\-][0-9]{1,2}[\/\-][0-9]{2,4})", RegexOptions.IgnoreCase).Groups?.Count > 2
            ? Regex.Match(text, @"(Issue\s*Date|Date)\s*[:\-]?\s*([0-9]{1,2}[\/\-][0-9]{1,2}[\/\-][0-9]{2,4})", RegexOptions.IgnoreCase).Groups[2].Value
            : "", out var issue);
        if (issue != default) dto.IssueDate = issue;

        DateTime.TryParse(Regex.Match(text, @"(Due\s*Date)\s*[:\-]?\s*([0-9]{1,2}[\/\-][0-9]{1,2}[\/\-][0-9]{2,4})", RegexOptions.IgnoreCase).Groups?.Count > 2
            ? Regex.Match(text, @"(Due\s*Date)\s*[:\-]?\s*([0-9]{1,2}[\/\-][0-9]{1,2}[\/\-][0-9]{2,4})", RegexOptions.IgnoreCase).Groups[2].Value
            : "", out var due);
        if (due != default) dto.DueDate = due;

        // Very simple item detection: lines that look like "Description  qty  price"
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^(?<desc>.+?)\s+(?<qty>\d{1,3})\s+(?<price>\d+(?:\.\d{1,2})?)\s*$");
            if (m.Success)
            {
                dto.Items.Add(new ExtractedItemDto
                {
                    Description = m.Groups["desc"].Value.Trim(),
                    Quantity = int.TryParse(m.Groups["qty"].Value, out var q) ? q : 1,
                    UnitPrice = decimal.TryParse(m.Groups["price"].Value, out var p) ? p : 0m
                });
            }
        }

        if (dto.Items.Count == 0)
        {
            dto.Items.Add(new ExtractedItemDto { Description = "Service", Quantity = 1, UnitPrice = 0m });
        }

        return Normalize(dto);
    }

    private static ExtractedInvoiceDto Normalize(ExtractedInvoiceDto dto)
    {
        dto.ClientName ??= "";
        dto.ClientEmail ??= "";
        dto.ClientPhone ??= "";
        dto.InvoiceNumber ??= "";
        dto.Notes ??= "";

        // Ensure at least one item
        dto.Items = dto.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Description))
            .Select(i => new ExtractedItemDto
            {
                Description = i.Description?.Trim(),
                Quantity = i.Quantity <= 0 ? 1 : i.Quantity,
                UnitPrice = i.UnitPrice < 0 ? 0 : i.UnitPrice
            })
            .ToList();

        if (dto.Items.Count == 0)
            dto.Items.Add(new ExtractedItemDto { Description = "Service", Quantity = 1, UnitPrice = 0m });

        return dto;
    }
}
