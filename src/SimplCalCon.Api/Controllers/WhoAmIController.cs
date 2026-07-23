using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using SimplCalCon.Api.Authentication;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Thin identity-echo endpoints proving each auth path end-to-end (this unit's goal;
/// ADR 0018). They are placeholders for the real resource controllers that arrive
/// with the ADR 0009 REST plumbing.
/// </summary>
public sealed class WhoAmIController : ControllerBase
{
    /// <summary>OIDC userinfo — proves the access token from the code+PKCE flow.</summary>
    [HttpGet("~/connect/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public IActionResult UserInfo() => Ok(Describe());

    /// <summary>Bearer-protected REST probe.</summary>
    [HttpGet("~/api/whoami")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public IActionResult ApiWhoAmI() => Ok(Describe());

    /// <summary>DAV Basic (app-password) probe.</summary>
    [HttpGet("~/dav/whoami")]
    [Authorize(AuthenticationSchemes = DavAuthenticationDefaults.Scheme)]
    public IActionResult DavWhoAmI() => Ok(Describe());

    private object Describe() => new
    {
        userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub"),
        email = User.FindFirstValue(ClaimTypes.Email)
            ?? User.FindFirstValue("email"),
        tenantId = User.FindFirstValue("tenant_id"),
    };
}
