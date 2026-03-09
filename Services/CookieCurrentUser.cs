namespace Invoxa.Web.Services;

public class CookieCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    public CookieCurrentUser(IHttpContextAccessor http) => _http = http;

    public string Name
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx is null) return "Admin";
            if (ctx.Request.Cookies.TryGetValue("invoxa_user", out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
            if (ctx.Request.Cookies.TryGetValue("invoxa_auth", out var email) && !string.IsNullOrWhiteSpace(email))
                return email;
            return "Admin";
        }
    }
}
