using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Invoxa.Web.Pages.Payments;

public class CancelModel : PageModel
{
    public string Token { get; set; } = "";

    public void OnGet(string? token = null)
    {
        Token = token ?? "";
    }
}
