using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Application.Abstractions.Identity;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Minimal interactive login for the OIDC authorization flow (ADR 0005). The page reuses
/// the hosted Blazor app's stylesheets (Bootstrap + <c>css/app.css</c>, served at root by
/// the Api) so it matches the web UI's look (ADR 0025).
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
            : $"""<div class="alert alert-danger" role="alert">{HtmlEncoder.Default.Encode(error)}</div>""";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>Sign in — SimplCalCon</title>
              <link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css" />
              <link rel="stylesheet" href="/css/app.css" />
              <link rel="icon" type="image/png" href="/favicon.png" />
            </head>
            <body class="bg-light">
              <main class="container" style="max-width: 24rem;">
                <div class="card shadow-sm mt-5">
                  <div class="card-body p-4">
                    <h1 class="h3 mb-4 text-center">SimplCalCon</h1>
                    {{errorBlock}}
                    <form method="post" action="/Account/Login">
                      <input type="hidden" name="returnUrl" value="{{encodedReturn}}" />
                      <div class="mb-3">
                        <label class="form-label" for="email">Email</label>
                        <input class="form-control" id="email" name="email" type="email" required autofocus />
                      </div>
                      <div class="mb-3">
                        <label class="form-label" for="password">Password</label>
                        <input class="form-control" id="password" name="password" type="password" required />
                      </div>
                      <button class="btn btn-primary w-100" type="submit">Sign in</button>
                    </form>
                  </div>
                </div>
              </main>
            </body>
            </html>
            """;
    }
}
