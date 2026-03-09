using Invoxa.Web.Data;

using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoxa.Web.Services;

namespace Invoxa.Web.Pages.Logs;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<LogVm> Logs { get; set; } = new();

    public async Task OnGet()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        Logs = await _db.ReminderLogs
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.SentAtUtc)
            .Take(200)
            .Select(r => new LogVm
            {
                When = r.SentAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                Actor = r.Actor,
                Channel = r.Channel,
                Type = r.Type,
                Target = r.To ?? "",
                Notes = r.Notes ?? ""
            })
            .ToListAsync();
    }


    public async Task<IActionResult> OnGetExport()
    {
        var companyId = await CurrentUserContext.RequireCompanyIdAsync(HttpContext, _db);
        var rows = await _db.ReminderLogs
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.SentAtUtc)
            .Take(2000)
            .Select(r => new
            {
                When = r.SentAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                User = r.Actor,
                Channel = r.Channel,
                Type = r.Type,
                Target = r.To ?? "",
                Notes = r.Notes ?? ""
            })
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Activity");
        ws.Cell(1, 1).Value = "When";
        ws.Cell(1, 2).Value = "User";
        ws.Cell(1, 3).Value = "Channel";
        ws.Cell(1, 4).Value = "Type";
        ws.Cell(1, 5).Value = "Target";
        ws.Cell(1, 6).Value = "Notes";

        for (int i = 0; i < rows.Count; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].When;
            ws.Cell(i + 2, 2).Value = rows[i].User;
            ws.Cell(i + 2, 3).Value = rows[i].Channel;
            ws.Cell(i + 2, 4).Value = rows[i].Type;
            ws.Cell(i + 2, 5).Value = rows[i].Target;
            ws.Cell(i + 2, 6).Value = rows[i].Notes;
        }

        ws.Columns().AdjustToContents();

        using var ms = new System.IO.MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();
        var fileName = $"invoxa-activity-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public class LogVm
    {
        public string When { get; set; } = "";
        public string Actor { get; set; } = "";
        public string Channel { get; set; } = "";
        public string Type { get; set; } = "";
        public string Target { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}