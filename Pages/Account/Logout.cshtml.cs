using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Invoxa.Web.Pages.Account;

public class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        var options = new CookieOptions { Path = "/" };
        Response.Cookies.Delete("invoxa_auth", options);
        Response.Cookies.Delete("invoxa_user", options);
        Response.Cookies.Delete("invoxa_role", options);
        return RedirectToPage("/Index", new { message = "You have been logged out successfully." });
    }
}
