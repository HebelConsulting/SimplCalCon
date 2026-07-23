using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Manages a collection's grants (ADR 0007, 0023). Requires the caller to own the
/// collection or hold the <c>share</c>/<c>admin</c> right. Typed sub-resource of both
/// calendars and address books.
/// </summary>
public sealed class SharesController(IAclService acl, IPrincipalDirectory directory) : ApiControllerBase(acl)
{
    [HttpGet("~/api/calendars/{collectionId:guid}/shares")]
    [HttpGet("~/api/address-books/{collectionId:guid}/shares")]
    public async Task<ActionResult<CollectionResource<ShareResource>>> List(
        Guid collectionId, CancellationToken cancellationToken)
    {
        await RequireCanShareAsync(collectionId, cancellationToken);

        var grants = await Acl.ListGrantsAsync(collectionId, cancellationToken);
        var principals = (await directory.GetAsync(grants.Select(g => g.PrincipalId).ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        var items = grants
            .Where(g => principals.ContainsKey(g.PrincipalId))
            .Select(g => Map(collectionId, g.PrincipalId, principals[g.PrincipalId], g.Rights))
            .ToList();

        return new CollectionResource<ShareResource>
        {
            Items = items,
            Links = { new Link("self", $"/api/calendars/{collectionId}/shares") },
        };
    }

    [HttpPut("~/api/calendars/{collectionId:guid}/shares/{principalId:guid}")]
    [HttpPut("~/api/address-books/{collectionId:guid}/shares/{principalId:guid}")]
    public async Task<ActionResult<ShareResource>> Put(
        Guid collectionId, Guid principalId, [FromBody] ShareWriteRequest request, CancellationToken cancellationToken)
    {
        await RequireCanShareAsync(collectionId, cancellationToken);

        var rights = AclRights.Parse(request.Rights);
        if (rights == AclRight.None)
        {
            return BadRequest("At least one valid right is required.");
        }

        // GrantAsync rejects cross-tenant principals (mapped to 400 by the exception handler).
        await Acl.GrantAsync(collectionId, principalId, rights, cancellationToken);

        var principal = (await directory.GetAsync([principalId], cancellationToken)).FirstOrDefault();
        return principal is null
            ? BadRequest("Unknown principal.")
            : Map(collectionId, principalId, principal, rights);
    }

    [HttpDelete("~/api/calendars/{collectionId:guid}/shares/{principalId:guid}")]
    [HttpDelete("~/api/address-books/{collectionId:guid}/shares/{principalId:guid}")]
    public async Task<IActionResult> Delete(Guid collectionId, Guid principalId, CancellationToken cancellationToken)
    {
        await RequireCanShareAsync(collectionId, cancellationToken);
        await Acl.RevokeAsync(collectionId, principalId, cancellationToken);
        return NoContent();
    }

    private static ShareResource Map(Guid collectionId, Guid principalId, PrincipalSummary principal, AclRight rights) => new()
    {
        PrincipalId = principalId,
        Kind = principal.Kind,
        DisplayName = principal.DisplayName,
        Email = principal.Email,
        Rights = AclRights.Format(rights),
        Links = { new Link("self", $"/api/calendars/{collectionId}/shares/{principalId}") },
    };
}
