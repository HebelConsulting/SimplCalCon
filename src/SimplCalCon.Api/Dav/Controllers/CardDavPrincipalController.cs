using System.Security.Claims;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>The principal resource: current-user-principal, principal-URL, addressbook-home-set.</summary>
public sealed class CardDavPrincipalController : DavControllerBase
{
    // macOS accountsd PROPFINDs the principal *collection* during setup (asking for
    // current-user-principal / principal-URL / resourcetype); answer it so discovery from
    // /dav/principals/ succeeds instead of 405 (RFC 3744 principal-collection-set).
    [HttpPropfind("~/dav/principals")]
    public async Task<IActionResult> PropfindCollection(CancellationToken cancellationToken)
    {
        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));

        var collection = new DavResource("/dav/principals/");
        collection.Set(DavNames.ResourceType, new XElement(DavNames.Collection));
        collection.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, PrincipalHref(CurrentUserId)));
        collection.Set(DavNames.PrincipalUrl, new XElement(DavNames.Href, PrincipalHref(CurrentUserId)));

        return DavXml.MultiStatus(MultiStatus.Build(request, [collection]));
    }

    [HttpPropfind("~/dav/principals/{userId:guid}")]
    public async Task<IActionResult> Propfind(Guid userId, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));
        var email = User.FindFirstValue(ClaimTypes.Email);
        var displayName = email ?? userId.ToString();
        var principal = CardDavResources.Principal(
            PrincipalHref(userId), HomeHref(userId), CalendarHomeHref(userId), displayName,
            email, $"{CalendarHomeHref(userId)}inbox/", $"{CalendarHomeHref(userId)}outbox/");

        return DavXml.MultiStatus(MultiStatus.Build(request, [principal]));
    }
}
