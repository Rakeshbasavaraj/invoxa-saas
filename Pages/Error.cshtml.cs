using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class ErrorModel : PageModel
{
    public string? ErrorMessage { get; private set; }
    public string? ErrorStack { get; private set; }

    public void OnGet()
    {
        // When routed via UseExceptionHandler("/Error"), ASP.NET Core populates the feature.
        var feature = HttpContext.Features.Get<IExceptionHandlerFeature>();
        if (feature?.Error != null)
        {
            ErrorMessage = feature.Error.Message;
            // Only show stack trace in Development.
            if (HttpContext.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true)
                ErrorStack = feature.Error.ToString();
        }
    }
}
