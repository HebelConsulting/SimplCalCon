using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>The principal resource: current-user-principal, principal-URL, addressbook-home-set.</summary>
public sealed class CardDavPrincipalController : DavControllerBase
{
    [HttpPropfind("~/dav/principals/{userId:guid}")]
    public async Task<IActionResult> Propfind(Guid userId, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));
        var displayName = User.FindFirstValue(ClaimTypes.Email) ?? userId.ToString();
        var principal = CardDavResources.Principal(PrincipalHref(userId), HomeHref(userId), displayName);

        return DavXml.MultiStatus(MultiStatus.Build(request, [principal]));
    }
}
