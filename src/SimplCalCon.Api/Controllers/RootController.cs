using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Controllers;

/// <summary>The public API entrypoint: the HATEOAS discovery document (ADR 0009).</summary>
[ApiController]
[Route("api")]
public sealed class RootController : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiRootResource> Get() => Discovery();

    [HttpHead]
    public IActionResult Head() => Ok();

    private static ApiRootResource Discovery() => new()
    {
        Links =
        {
            new Link("self", "/api"),
            new Link("me", "/api/me"),
            new Link("calendars", "/api/calendars"),
            new Link("address-books", "/api/address-books"),
            new Link("app-passwords", "/api/app-passwords"),
            new Link("openapi", "/openapi/v1.json"),
        },
    };
}
