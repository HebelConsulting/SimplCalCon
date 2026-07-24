using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>Service discovery: `/.well-known/carddav`, OPTIONS capabilities, and the `/dav` root.</summary>
public sealed class CardDavServiceController : DavControllerBase
{
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
        Response.Headers["DAV"] = "1, 3, addressbook, calendar-access";
        Response.Headers["Allow"] = "OPTIONS, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT, MKCOL, MKCALENDAR";
        return Ok();
    }

    [HttpPropfind("~/dav")]
    public async Task<IActionResult> Propfind(CancellationToken cancellationToken)
    {
        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));

        var root = new DavResource("/dav/");
        root.Set(DavNames.ResourceType, new XElement(DavNames.Collection));
        root.Set(DavNames.CurrentUserPrincipal, new XElement(DavNames.Href, PrincipalHref(CurrentUserId)));
        root.Set(DavNames.PrincipalUrl, new XElement(DavNames.Href, PrincipalHref(CurrentUserId)));

        return DavXml.MultiStatus(MultiStatus.Build(request, [root]));
    }
}
