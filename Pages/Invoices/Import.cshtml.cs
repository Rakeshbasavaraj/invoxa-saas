using ClosedXML.Excel;
using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Invoxa.Web.Pages.Invoices;

public class ImportModel : PageModel
{
    private readonly AppDbContext _db;
    public ImportModel(AppDbContext db) => _db = db;

    public int ImportedRowCount { get; set; }
    public int CreatedInvoiceCount { get; set; }
    public List<FailedInvoiceRow> FailedRows { get; set; } = new();

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public void OnGet() => LoadFailedRowsFromTempData();

    public async Task<IActionResult> OnPostAsync(IFormFile? upload)
    {
        if (upload == null || upload.Length == 0)
        {
            ErrorMessage = "Please select a CSV or XLSX file.";
            return Page();
        }

        var ext = Path.GetExtension(upload.FileName).ToLowerInvariant();
        List<InvoiceImportRow> rows;
        await using var stream = upload.OpenReadStream();
        if (ext == ".csv") rows = await ReadCsvAsync(stream);
        else if (ext == ".xlsx") rows = ReadExcel(stream);
        else
        {
            ErrorMessage = "Unsupported file type. Please upload CSV or XLSX.";
            return Page();
        }

        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var company = await _db.Companies.FirstAsync(c => c.Id == companyId);
        var clients = await _db.Clients.Where(c => c.CompanyId == companyId).ToListAsync();
        var clientsByEmail = clients.Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .ToDictionary(c => c.Email!.Trim().ToLowerInvariant(), c => c);
        var clientsByName = clients.ToDictionary(c => c.Name.Trim().ToLowerInvariant(), c => c);
        var existingInvoiceNumbers = await _db.Invoices.Where(i => i.CompanyId == companyId).Select(i => i.InvoiceNumber).ToListAsync();
        var existingInvoiceSet = existingInvoiceNumbers.Select(x => x.Trim().ToLowerInvariant()).ToHashSet();

        FailedRows = new();
        ImportedRowCount = 0;
        CreatedInvoiceCount = 0;

        var grouped = rows.GroupBy(r => (r.InvoiceNo ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
        var invoicesToAdd = new List<Invoice>();
        var batchClientEmailSet = new HashSet<string>();
        var batchClientNameSet = new HashSet<string>();

        foreach (var group in grouped)
        {
            var invoiceNo = group.Key;
            var groupRows = group.ToList();
            var groupErrors = ValidateGroup(groupRows, existingInvoiceSet);
            if (groupErrors.Any())
            {
                foreach (var row in groupRows)
                {
                    FailedRows.Add(new FailedInvoiceRow
                    {
                        RowNumber = row.RowNumber,
                        InvoiceNo = row.InvoiceNo,
                        ClientEmail = row.ClientEmail,
                        ClientName = row.ClientName,
                        Description = row.Description,
                        Error = string.Join("; ", groupErrors)
                    });
                }
                continue;
            }

            var first = groupRows[0];
            var normalizedEmail = Normalize(first.ClientEmail);
            var normalizedName = Normalize(first.ClientName);
            Client? client = null;
            if (!string.IsNullOrWhiteSpace(normalizedEmail) && clientsByEmail.TryGetValue(normalizedEmail, out var emailClient)) client = emailClient;
            else if (!string.IsNullOrWhiteSpace(normalizedName) && clientsByName.TryGetValue(normalizedName, out var nameClient)) client = nameClient;

            if (client == null)
            {
                client = new Client
                {
                    CompanyId = companyId,
                    Name = string.IsNullOrWhiteSpace(first.ClientName) ? (first.ClientEmail ?? "Imported Client") : first.ClientName!.Trim(),
                    Email = first.ClientEmail?.Trim(),
                    Country = first.Country?.Trim(),
                    City = first.City?.Trim(),
                    PortalToken = Guid.NewGuid().ToString("N")
                };
                _db.Clients.Add(client);
                if (!string.IsNullOrWhiteSpace(normalizedEmail)) clientsByEmail[normalizedEmail] = client;
                if (!string.IsNullOrWhiteSpace(normalizedName)) clientsByName[normalizedName] = client;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(client.Country) && !string.IsNullOrWhiteSpace(first.Country)) client.Country = first.Country.Trim();
                if (string.IsNullOrWhiteSpace(client.City) && !string.IsNullOrWhiteSpace(first.City)) client.City = first.City.Trim();
            }

            var invoice = new Invoice
            {
                CompanyId = companyId,
                Client = client,
                InvoiceNumber = invoiceNo,
                PublicToken = Guid.NewGuid().ToString("N"),
                IssueDate = ParseDate(first.IssueDate) ?? DateOnly.FromDateTime(DateTime.Today),
                DueDate = ParseDate(first.DueDate) ?? DateOnly.FromDateTime(DateTime.Today),
                Status = ParseStatus(first.Status, first.DueDate),
                Notes = "Imported from file"
            };

            foreach (var row in groupRows)
            {
                invoice.Items.Add(new InvoiceItem
                {
                    Description = row.Description!.Trim(),
                    Quantity = int.Parse(row.Qty!, CultureInfo.InvariantCulture),
                    UnitPrice = decimal.Parse(row.UnitPrice!, CultureInfo.InvariantCulture)
                });
                ImportedRowCount++;
            }

            invoicesToAdd.Add(invoice);
            CreatedInvoiceCount++;
            existingInvoiceSet.Add(invoiceNo.Trim().ToLowerInvariant());
            company.NextInvoiceNumber = Math.Max(company.NextInvoiceNumber, ExtractSuggestedNextNumber(invoiceNo));
        }

        if (invoicesToAdd.Any())
        {
            await _db.Invoices.AddRangeAsync(invoicesToAdd);
            await _db.SaveChangesAsync();

            foreach (var invoice in invoicesToAdd)
            {
                _db.ReminderLogs.Add(new ReminderLog
                {
                    CompanyId = companyId,
                    InvoiceId = invoice.Id,
                    Actor = "Admin",
                    Channel = "Import",
                    Type = "InvoiceImported",
                    To = invoice.InvoiceNumber,
                    Notes = $"Imported {invoice.Items.Count} item(s) for {invoice.InvoiceNumber}"
                });
            }
            await _db.SaveChangesAsync();
        }

        StatusMessage = CreatedInvoiceCount > 0 ? $"Created {CreatedInvoiceCount} invoice(s) from {ImportedRowCount} row(s)." : null;
        if (FailedRows.Count > 0)
        {
            ErrorMessage = $"{FailedRows.Count} row(s) were skipped and not added.";
            TempData["FailedInvoiceRowsJson"] = JsonSerializer.Serialize(FailedRows);
        }
        else
        {
            TempData.Remove("FailedInvoiceRowsJson");
        }

        return RedirectToPage();
    }

    public IActionResult OnGetDownloadSample()
    {
        var csv = "InvoiceNo,ClientEmail,ClientName,Country,City,IssueDate,DueDate,Description,Qty,UnitPrice,Status\n" +
                  "INV-1001,rakesh@example.com,Rakesh,India,Chitradurga,2026-03-12,2026-03-19,Service,10,89,Paid\n" +
                  "INV-1001,rakesh@example.com,Rakesh,India,Chitradurga,2026-03-12,2026-03-19,UI/UX Design Service,10,9787,Paid\n" +
                  "INV-1001,rakesh@example.com,Rakesh,India,Chitradurga,2026-03-12,2026-03-19,Automation Consulting,10,100,Paid\n" +
                  "INV-1002,gulf@example.com,Gulf Tech,Kuwait,Kuwait City,2026-03-12,2026-03-20,Hosting,2,100,Pending\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "sample-invoices.csv");
    }

    public IActionResult OnGetDownloadFailed()
    {
        var failedJson = TempData.Peek("FailedInvoiceRowsJson") as string;
        if (string.IsNullOrWhiteSpace(failedJson)) return RedirectToPage();
        var rows = JsonSerializer.Deserialize<List<FailedInvoiceRow>>(failedJson) ?? new List<FailedInvoiceRow>();
        var sb = new StringBuilder();
        sb.AppendLine("RowNumber,InvoiceNo,ClientEmail,ClientName,Description,Error");
        foreach (var row in rows)
            sb.AppendLine($"{row.RowNumber},{Escape(row.InvoiceNo)},{Escape(row.ClientEmail)},{Escape(row.ClientName)},{Escape(row.Description)},{Escape(row.Error)}");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "failed-invoices.csv");
    }

    private void LoadFailedRowsFromTempData()
    {
        var failedJson = TempData.Peek("FailedInvoiceRowsJson") as string;
        FailedRows = string.IsNullOrWhiteSpace(failedJson)
            ? new List<FailedInvoiceRow>()
            : (JsonSerializer.Deserialize<List<FailedInvoiceRow>>(failedJson) ?? new List<FailedInvoiceRow>());
    }

    private static List<string> ValidateGroup(List<InvoiceImportRow> rows, HashSet<string> existingInvoiceSet)
    {
        var errors = new List<string>();
        var first = rows[0];
        var invoiceNo = (first.InvoiceNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(invoiceNo)) errors.Add("InvoiceNo is required.");
        if (!string.IsNullOrWhiteSpace(invoiceNo) && existingInvoiceSet.Contains(invoiceNo.ToLowerInvariant())) errors.Add("Invoice number already exists.");
        if (string.IsNullOrWhiteSpace(first.ClientEmail) && string.IsNullOrWhiteSpace(first.ClientName)) errors.Add("ClientEmail or ClientName is required.");
        if (!string.IsNullOrWhiteSpace(first.ClientEmail))
        {
            try { _ = new System.Net.Mail.MailAddress(first.ClientEmail.Trim()); }
            catch { errors.Add("ClientEmail format is invalid."); }
        }

        var clientEmails = rows.Select(r => Normalize(r.ClientEmail)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var clientNames = rows.Select(r => Normalize(r.ClientName)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var dueDates = rows.Select(r => Normalize(r.DueDate)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var issueDates = rows.Select(r => Normalize(r.IssueDate)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();

        if (clientEmails.Count > 1 || clientNames.Count > 1) errors.Add("Same InvoiceNo cannot belong to multiple clients.");
        if (dueDates.Count > 1) errors.Add("Same InvoiceNo cannot have multiple due dates.");
        if (issueDates.Count > 1) errors.Add("Same InvoiceNo cannot have multiple issue dates.");

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Description)) errors.Add($"Row {row.RowNumber}: Description is required.");
            if (!int.TryParse(row.Qty, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty <= 0) errors.Add($"Row {row.RowNumber}: Qty must be greater than 0.");
            if (!decimal.TryParse(row.UnitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0) errors.Add($"Row {row.RowNumber}: UnitPrice is invalid.");
            if (!string.IsNullOrWhiteSpace(row.IssueDate) && ParseDate(row.IssueDate) == null) errors.Add($"Row {row.RowNumber}: IssueDate is invalid.");
            if (!string.IsNullOrWhiteSpace(row.DueDate) && ParseDate(row.DueDate) == null) errors.Add($"Row {row.RowNumber}: DueDate is invalid.");
        }

        return errors.Distinct().ToList();
    }

    private static InvoiceStatus ParseStatus(string? value, string? dueDate)
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<InvoiceStatus>(value, true, out var parsed)) return parsed;
        var due = ParseDate(dueDate);
        if (due.HasValue && due.Value < DateOnly.FromDateTime(DateTime.Today)) return InvoiceStatus.Overdue;
        return InvoiceStatus.Unpaid;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParse(value, out var dateOnly)) return dateOnly;
        if (DateTime.TryParse(value, out var dateTime)) return DateOnly.FromDateTime(dateTime);
        return null;
    }

    private static int ExtractSuggestedNextNumber(string invoiceNo)
    {
        var digits = new string(invoiceNo.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var value) ? value + 1 : 1;
    }

    private static async Task<List<InvoiceImportRow>> ReadCsvAsync(Stream stream)
    {
        var rows = new List<InvoiceImportRow>();
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var all = await reader.ReadToEndAsync();
        var lines = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return rows;
        var header = SplitCsvLine(lines[0]);
        var map = BuildHeaderMap(header);
        for (var i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsvLine(lines[i]);
            rows.Add(MapRow(i + 1, cols, map));
        }
        return rows;
    }

    private static List<InvoiceImportRow> ReadExcel(Stream stream)
    {
        var rows = new List<InvoiceImportRow>();
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow == 0) return rows;
        var headers = ws.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        var map = BuildHeaderMap(headers);
        for (var r = 2; r <= lastRow; r++)
        {
            var cols = headers.Select((_, idx) => ws.Cell(r, idx + 1).GetString()).ToList();
            rows.Add(MapRow(r, cols, map));
        }
        return rows;
    }

    private static InvoiceImportRow MapRow(int rowNumber, List<string> cols, Dictionary<string, int> map) => new()
    {
        RowNumber = rowNumber,
        InvoiceNo = GetValue(cols, map, "invoiceno"),
        ClientEmail = GetValue(cols, map, "clientemail"),
        ClientName = GetValue(cols, map, "clientname"),
        Country = GetValue(cols, map, "country"),
        City = GetValue(cols, map, "city"),
        IssueDate = GetValue(cols, map, "issuedate"),
        DueDate = GetValue(cols, map, "duedate"),
        Description = GetValue(cols, map, "description"),
        Qty = GetValue(cols, map, "qty"),
        UnitPrice = GetValue(cols, map, "unitprice"),
        Status = GetValue(cols, map, "status")
    };

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var key = (headers[i] ?? string.Empty).Trim().Replace(" ", string.Empty).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key)) map[key] = i;
        }
        return map;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }

    private static string? GetValue(List<string> cols, Dictionary<string, int> map, string key)
        => map.TryGetValue(key, out var idx) && idx < cols.Count ? cols[idx]?.Trim() : null;

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string Escape(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n')) return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }

    public class InvoiceImportRow
    {
        public int RowNumber { get; set; }
        public string? InvoiceNo { get; set; }
        public string? ClientEmail { get; set; }
        public string? ClientName { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? IssueDate { get; set; }
        public string? DueDate { get; set; }
        public string? Description { get; set; }
        public string? Qty { get; set; }
        public string? UnitPrice { get; set; }
        public string? Status { get; set; }
    }

    public class FailedInvoiceRow
    {
        public int RowNumber { get; set; }
        public string? InvoiceNo { get; set; }
        public string? ClientEmail { get; set; }
        public string? ClientName { get; set; }
        public string? Description { get; set; }
        public string Error { get; set; } = string.Empty;
    }
}
