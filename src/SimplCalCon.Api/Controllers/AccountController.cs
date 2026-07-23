using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Application.Abstractions.Identity;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Minimal interactive login for the OIDC authorization flow (ADR 0005). The page is
/// deliberately plain; it gets re-dressed when the web UI / ADR 0009 plumbing lands.
/// </summary>
public sealed class AccountController(IUserAuthenticationService authentication) : ControllerBase
{
    [HttpGet("~/Account/Login")]
    public IActionResult Login([FromQuery] string? returnUrl)
        => Content(RenderForm(returnUrl, error: null), "text/html");

    [HttpPost("~/Account/Login")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Login(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await authentication.AuthenticateAsync(email, password, cancellationToken);
        if (result.Status != UserAuthenticationStatus.Success)
        {
            var message = result.Status switch
            {
                UserAuthenticationStatus.LockedOut => "Account temporarily locked. Try again later.",
                UserAuthenticationStatus.Disabled => "Account is disabled.",
                _ => "Invalid email or password.",
            };

            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Content(RenderForm(returnUrl, message), "text/html");
        }

        var user = result.User!;
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }

    private static string RenderForm(string? returnUrl, string? error)
    {
        var encodedReturn = HtmlEncoder.Default.Encode(returnUrl ?? string.Empty);
        var errorBlock = error is null
            ? string.Empty
            : $"<p style=\"color:#b00\">{HtmlEncoder.Default.Encode(error)}</p>";

        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><title>Sign in — SimplCalCon</title></head>
            <body style="font-family:system-ui;max-width:22rem;margin:4rem auto">
            <h1>SimplCalCon</h1>
            {{errorBlock}}
            <form method="post" action="/Account/Login">
              <input type="hidden" name="returnUrl" value="{{encodedReturn}}" />
              <p><label>Email<br><input name="email" type="email" required autofocus style="width:100%"></label></p>
              <p><label>Password<br><input name="password" type="password" required style="width:100%"></label></p>
              <p><button type="submit">Sign in</button></p>
            </form>
            </body></html>
            """;
    }
}
