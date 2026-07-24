using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>Service discovery: `/.well-known/carddav`, OPTIONS capabilities, and the `/dav` root.</summary>
public sealed class CardDavServiceController : DavControllerBase
{
    private const string DavHeader = "1, 3, addressbook, calendar-access";
    private const string DavAllow = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, REPORT, MKCOL, MKCALENDAR";

    [AllowAnonymous]
    [HttpGet("~/.well-known/carddav")]
    [HttpPropfind("~/.well-known/carddav")]
    public IActionResult WellKnown()
    {
        Response.Headers.Location = "/dav/";
        return StatusCode(StatusCodes.Status301MovedPermanently);
    }

    [AllowAnonymous]
    [HttpOptions("~/dav")]
    [HttpOptions("~/dav/{**path}")]
    public IActionResult Options()
    {
        Response.Headers["DAV"] = DavHeader;
        Response.Headers["Allow"] = DavAllow;
        return Ok();
    }

    // macOS Contacts (RFC 6764 §6) probes OPTIONS on the bare server root and requires a
    // DAV header advertising `addressbook` before it will finalize the account — otherwise
    // the account is discarded (no sync, absent from "Default Account"). The Calendar client
    // skips this step, which is why CalDAV worked while CardDAV didn't. Advertise here so the
    // root (which otherwise falls through to the SPA, GET/HEAD only) answers the probe.
    [AllowAnonymous]
    [HttpOptions("~/")]
    public IActionResult RootOptions()
    {
        Response.Headers["DAV"] = DavHeader;
        Response.Headers["Allow"] = DavAllow;
        return Ok();
    }

    [HttpPropfind("~/dav")]
    public async Task<IActionResult> Propfind(CancellationToken cancellationToken) =>
        await PrincipalDiscovery("/dav/", cancellationToken);

    // Root PROPFIND: RFC 6764 context-path discovery. Returns current-user-principal so a
    // client that starts at "/" (rather than /.well-known/carddav) can find the principal.
    [HttpPropfind("~/")]
    public async Task<IActionResult> RootPropfind(CancellationToken cancellationToken) =>
        await PrincipalDiscovery("/", cancellationToken);

    private async Task<IActionResult> PrincipalDiscovery(string href, CancellationToken cancellationToken)
    {
        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));

        var root = new DavResource(href);
        root.Set(DavNames.ResourceType, new XElement(DavNames.Collection));
        root.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, PrincipalHref(CurrentUserId)));
        root.Set(DavNames.PrincipalUrl, new XElement(DavNames.Href, PrincipalHref(CurrentUserId)));

        return DavXml.MultiStatus(MultiStatus.Build(request, [root]));
    }
}
