using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore; // OpenIddict HttpContext helpers (GetOpenIddictServerRequest) live here
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// OpenIddict authorization-code + PKCE + refresh flow (ADR 0005). The interactive
/// login is a cookie session established by <see cref="AccountController"/>; this
/// controller turns that session into tokens and re-checks the account on every
/// token exchange.
/// </summary>
public sealed class AuthorizationController(SimplCalConDbContext dbContext) : ControllerBase
{
    private const string IdentityAuthenticationType = "SimplCalCon";

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var cookie = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!cookie.Succeeded)
        {
            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(Request.Query),
                },
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        var userId = cookie.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await FindActiveUserAsync(userId);
        if (user is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Challenge(
                new AuthenticationProperties { RedirectUri = Request.PathBase + Request.Path + QueryString.Create(Request.Query) },
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        var identity = BuildIdentity(user, request.GetScopes());
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("The specified grant type is not supported.");
        }

        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var userId = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? result.Principal?.GetClaim(Claims.Subject);

        var user = await FindActiveUserAsync(userId);
        if (user is null)
        {
            return Forbid(
                new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                        "The account is no longer valid.",
                }),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var identity = BuildIdentity(user, result.Principal!.GetScopes());
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<User?> FindActiveUserAsync(string? userId)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            return null;
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == id);
        return user is { Status: UserStatus.Active } ? user : null;
    }

    private static ClaimsIdentity BuildIdentity(User user, ImmutableArray<string> scopes)
    {
        var identity = new ClaimsIdentity(
            authenticationType: IdentityAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString());
        identity.SetClaim(Claims.Email, user.Email);
        identity.SetClaim(Claims.Name, user.DisplayName);

        if (user.TenantId is { } tenantId)
        {
            identity.SetClaim("tenant_id", tenantId.ToString());
        }

        identity.SetClaim(Claims.Role, user.IsPlatformAdministrator
            ? "platform_admin"
            : user.TenantRole?.ToString().ToLowerInvariant());

        identity.SetScopes(scopes);
        identity.SetDestinations(GetDestinations);
        return identity;
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name:
            case Claims.Email:
            case Claims.Role:
            case "tenant_id":
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Profile))
                {
                    yield return Destinations.IdentityToken;
                }

                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
