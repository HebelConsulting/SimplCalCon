using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions.Identity;

namespace SimplCalCon.Api.Authentication;

/// <summary>
/// HTTP Basic authentication for the DAV surface, backed by per-device app passwords
/// (ADR 0005). Account passwords are never accepted here.
/// </summary>
public sealed class DavBasicAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDavCredentialAuthenticator authenticator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BasicPrefix = "Basic ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header))
        {
            return AuthenticateResult.NoResult();
        }

        var value = header.ToString();
        if (!value.StartsWith(BasicPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value[BasicPrefix.Length..].Trim()));
        }
        catch (FormatException)
        {
            return AuthenticateResult.Fail("Malformed Basic credentials.");
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
        {
            return AuthenticateResult.Fail("Malformed Basic credentials.");
        }

        var email = decoded[..separator];
        var secret = decoded[(separator + 1)..];

        var identity = await authenticator.AuthenticateAsync(email, secret, Context.RequestAborted);
        if (identity is null)
        {
            return AuthenticateResult.Fail("Invalid DAV credentials.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new(ClaimTypes.Email, identity.Email),
        };

        if (identity.TenantId is { } tenantId)
        {
            claims.Add(new Claim("tenant_id", tenantId.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = $"Basic realm=\"{DavAuthenticationDefaults.Realm}\", charset=\"UTF-8\"";
        return Task.CompletedTask;
    }
}
