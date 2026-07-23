using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.AppPasswords;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Authentication;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// The current user's per-device DAV app passwords (ADR 0005). The secret is returned
/// once, at creation; revocation is a DELETE guarded by ETag/If-Match (ADR 0009).
/// </summary>
[ApiController]
[Route("api/app-passwords")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class AppPasswordsController(IAppPasswordService appPasswords) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<AppPasswordResource>>> List(CancellationToken cancellationToken)
    {
        var items = await appPasswords.ListAsync(User.GetUserId(), cancellationToken);
        return new CollectionResource<AppPasswordResource>
        {
            Items = items.Select(Map).ToList(),
            Links = { new Link("self", "/api/app-passwords") },
        };
    }

    [HttpHead]
    public IActionResult HeadList() => Ok();

    [HttpGet("{id:guid}", Name = "GetAppPassword")]
    public async Task<ActionResult<AppPasswordResource>> Get(Guid id, CancellationToken cancellationToken)
    {
        var appPassword = await appPasswords.GetAsync(User.GetUserId(), id, cancellationToken);
        return appPassword is null ? throw new AppPasswordNotFoundException(id) : Map(appPassword);
    }

    [HttpHead("{id:guid}")]
    public async Task<IActionResult> Head(Guid id, CancellationToken cancellationToken)
    {
        var appPassword = await appPasswords.GetAsync(User.GetUserId(), id, cancellationToken);
        return appPassword is null ? throw new AppPasswordNotFoundException(id) : Ok();
    }

    [HttpPost]
    public async Task<ActionResult<AppPasswordCreatedResource>> Create(
        [FromBody] AppPasswordCreateRequest request, CancellationToken cancellationToken)
    {
        var issued = await appPasswords.IssueAsync(User.GetUserId(), request.Label, cancellationToken);
        var resource = new AppPasswordCreatedResource
        {
            Id = issued.AppPassword.Id,
            Label = issued.AppPassword.Label,
            CreatedAt = issued.AppPassword.CreatedAt,
            LastUsedAt = issued.AppPassword.LastUsedAt,
            ConcurrencyToken = issued.AppPassword.ConcurrencyToken,
            Secret = issued.Secret,
            Links = LinksFor(issued.AppPassword.Id),
        };

        return CreatedAtRoute("GetAppPassword", new { id = resource.Id }, resource);
    }

    [HttpDelete("{id:guid}")]
    [RequireIfMatch]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var token = RequireIfMatchAttribute.ReadToken(HttpContext);
        var revoked = await appPasswords.RevokeAsync(User.GetUserId(), id, token, cancellationToken);
        return revoked ? NoContent() : throw new AppPasswordNotFoundException(id);
    }

    private static AppPasswordResource Map(AppPassword appPassword) => new()
    {
        Id = appPassword.Id,
        Label = appPassword.Label,
        CreatedAt = appPassword.CreatedAt,
        LastUsedAt = appPassword.LastUsedAt,
        ConcurrencyToken = appPassword.ConcurrencyToken,
        Links = LinksFor(appPassword.Id),
    };

    private static List<Link> LinksFor(Guid id) =>
    [
        new Link("self", $"/api/app-passwords/{id}"),
        new Link("revoke", $"/api/app-passwords/{id}", "DELETE"),
        new Link("collection", "/api/app-passwords"),
    ];
}
