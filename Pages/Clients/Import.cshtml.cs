using ClosedXML.Excel;
using Invoxa.Web.Data;
using Invoxa.Web.Domain;
using Invoxa.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace Invoxa.Web.Pages.Clients;

public class ImportModel : PageModel
{
    private readonly AppDbContext _db;
    public ImportModel(AppDbContext db) => _db = db;

    public int ImportedCount { get; set; }
    public int ExistingClientCount { get; set; }
    public List<FailedClientRow> FailedRows { get; set; } = new();

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadExistingCountAsync();
        LoadFailedRowsFromTempData();
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? upload)
    {
        await LoadExistingCountAsync();

        if (upload == null || upload.Length == 0)
        {
            ErrorMessage = "Please select a CSV or XLSX file.";
            return Page();
        }

        var ext = Path.GetExtension(upload.FileName).ToLowerInvariant();
        List<ClientImportRow> rows;
        await using var stream = upload.OpenReadStream();
        if (ext == ".csv") rows = await ReadCsvAsync(stream);
        else if (ext == ".xlsx") rows = ReadExcel(stream);
        else
        {
            ErrorMessage = "Unsupported file type. Please upload CSV or XLSX.";
            return Page();
        }

        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var existingEmails = await _db.Clients
            .Where(c => c.CompanyId == companyId && c.Email != null)
            .Select(c => c.Email!.Trim().ToLower())
            .ToListAsync();
        var existingSet = existingEmails.ToHashSet();
        var batchSet = new HashSet<string>();

        var clientsToAdd = new List<Client>();
        FailedRows = new();
        ImportedCount = 0;

        foreach (var row in rows)
        {
            var error = ValidateRow(row, existingSet, batchSet);
            if (error != null)
            {
                FailedRows.Add(new FailedClientRow
                {
                    RowNumber = row.RowNumber,
                    ClientName = row.ClientName,
                    Email = row.Email,
                    Country = row.Country,
                    Error = error
                });
                continue;
            }

            var normalizedEmail = Normalize(row.Email);
            if (!string.IsNullOrWhiteSpace(normalizedEmail)) batchSet.Add(normalizedEmail);

            clientsToAdd.Add(new Client
            {
                CompanyId = companyId,
                Name = row.ClientName.Trim(),
                Email = row.Email?.Trim(),
                Phone = row.Phone?.Trim(),
                Country = row.Country?.Trim(),
                City = row.City?.Trim(),
                AddressLine1 = row.Address?.Trim(),
                PortalToken = Guid.NewGuid().ToString("N")
            });
        }

        if (clientsToAdd.Count > 0)
        {
            await _db.Clients.AddRangeAsync(clientsToAdd);
            await _db.SaveChangesAsync();
        }

        ImportedCount = clientsToAdd.Count;
        ExistingClientCount = await _db.Clients.CountAsync(c => c.CompanyId == companyId);

        StatusMessage = ImportedCount > 0 ? $"Imported {ImportedCount} client(s)." : null;
        if (FailedRows.Count > 0)
        {
            ErrorMessage = $"{FailedRows.Count} row(s) were skipped and not added.";
            TempData["FailedRowsJson"] = JsonSerializer.Serialize(FailedRows);
        }
        else
        {
            TempData.Remove("FailedRowsJson");
        }

        return RedirectToPage();
    }

    public IActionResult OnGetDownloadSample()
    {
        var csv = "ClientName,Email,Phone,Country,City,Address\nGulf Tech,gulf@example.com,9876543210,Kuwait,Kuwait City,Block 1\nABC Trading,abc@example.com,9988776655,India,Bengaluru,MG Road\n";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "sample-clients.csv");
    }

    public IActionResult OnGetDownloadFailed()
    {
        var failedJson = TempData.Peek("FailedRowsJson") as string;
        if (string.IsNullOrWhiteSpace(failedJson))
        {
            return RedirectToPage();
        }

        var rows = JsonSerializer.Deserialize<List<FailedClientRow>>(failedJson) ?? new List<FailedClientRow>();
        var sb = new StringBuilder();
        sb.AppendLine("RowNumber,ClientName,Email,Country,Error");
        foreach (var row in rows)
        {
            sb.AppendLine($"{row.RowNumber},{Escape(row.ClientName)},{Escape(row.Email)},{Escape(row.Country)},{Escape(row.Error)}");
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "failed-clients.csv");
    }

    private void LoadFailedRowsFromTempData()
    {
        var failedJson = TempData.Peek("FailedRowsJson") as string;
        FailedRows = string.IsNullOrWhiteSpace(failedJson)
            ? new List<FailedClientRow>()
            : (JsonSerializer.Deserialize<List<FailedClientRow>>(failedJson) ?? new List<FailedClientRow>());
    }

    private async Task LoadExistingCountAsync()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        ExistingClientCount = await _db.Clients.CountAsync(c => c.CompanyId == companyId);
    }

    private static string? ValidateRow(ClientImportRow row, HashSet<string> existingSet, HashSet<string> batchSet)
    {
        if (string.IsNullOrWhiteSpace(row.ClientName)) return "Client name is required.";
        if (string.IsNullOrWhiteSpace(row.Email)) return "Email is required.";

        var email = Normalize(row.Email);
        if (string.IsNullOrWhiteSpace(email)) return "Email is required.";
        try { _ = new System.Net.Mail.MailAddress(email); }
        catch { return "Email format is invalid."; }

        if (string.IsNullOrWhiteSpace(row.Country)) return "Country is required.";
        if (existingSet.Contains(email)) return "Email already exists.";
        if (batchSet.Contains(email)) return "Duplicate email in uploaded file.";
        return null;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static async Task<List<ClientImportRow>> ReadCsvAsync(Stream stream)
    {
        var rows = new List<ClientImportRow>();
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var all = await reader.ReadToEndAsync();
        var lines = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return rows;

        var header = SplitCsvLine(lines[0]);
        var map = BuildHeaderMap(header);
        for (var i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsvLine(lines[i]);
            rows.Add(new ClientImportRow
            {
                RowNumber = i + 1,
                ClientName = GetValue(cols, map, "clientname"),
                Email = GetValue(cols, map, "email"),
                Phone = GetValue(cols, map, "phone"),
                Country = GetValue(cols, map, "country"),
                City = GetValue(cols, map, "city"),
                Address = GetValue(cols, map, "address")
            });
        }
        return rows;
    }

    private static List<ClientImportRow> ReadExcel(Stream stream)
    {
        var rows = new List<ClientImportRow>();
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.First();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow == 0) return rows;

        var headers = ws.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        var map = BuildHeaderMap(headers);
        for (var r = 2; r <= lastRow; r++)
        {
            rows.Add(new ClientImportRow
            {
                RowNumber = r,
                ClientName = GetCell(ws, r, map, "clientname"),
                Email = GetCell(ws, r, map, "email"),
                Phone = GetCell(ws, r, map, "phone"),
                Country = GetCell(ws, r, map, "country"),
                City = GetCell(ws, r, map, "city"),
                Address = GetCell(ws, r, map, "address")
            });
        }
        return rows;
    }

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
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
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
    {
        return map.TryGetValue(key, out var idx) && idx < cols.Count ? cols[idx]?.Trim() : null;
    }

    private static string? GetCell(IXLWorksheet ws, int row, Dictionary<string, int> map, string key)
    {
        return map.TryGetValue(key, out var idx) ? ws.Cell(row, idx + 1).GetString().Trim() : null;
    }

    private static string Escape(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
        {
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
        return v;
    }

    public class ClientImportRow
    {
        public int RowNumber { get; set; }
        public string? ClientName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
        public string? Address { get; set; }
    }

    public class FailedClientRow
    {
        public int RowNumber { get; set; }
        public string? ClientName { get; set; }
        public string? Email { get; set; }
        public string? Country { get; set; }
        public string Error { get; set; } = string.Empty;
    }
}
