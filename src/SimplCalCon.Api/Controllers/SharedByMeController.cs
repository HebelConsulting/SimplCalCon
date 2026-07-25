using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;

namespace SimplCalCon.Api.Controllers;

/// <summary>The caller's own calendars/address books that they have shared with others (ADR 0058) — the owner counterpart to shared-with-me.</summary>
[Route("api/shared-by-me")]
public sealed class SharedByMeController(
    IDavRepository repository, IPrincipalDirectory directory, IAclService acl) : ApiControllerBase(acl)
{
    [HttpGet]
    [HttpHead]
    public async Task<ActionResult<CollectionResource<SharedByMeResource>>> List(CancellationToken cancellationToken)
    {
        var owned = (await repository.ListAccessibleCalendarsAsync(CurrentUserId, cancellationToken))
            .Where(c => c.OwnerId == CurrentUserId)
            .Select(c => (Kind: "calendars", Collection: (Collection)c))
            .Concat((await repository.ListAccessibleAddressBooksAsync(CurrentUserId, cancellationToken))
                .Where(a => a.OwnerId == CurrentUserId)
                .Select(a => (Kind: "address-books", Collection: (Collection)a)))
            .ToList();

        var items = new List<SharedByMeResource>();
        foreach (var (kind, collection) in owned)
        {
            var grants = await Acl.ListGrantsAsync(collection.Id, cancellationToken);
            if (grants.Count == 0)
            {
                continue;
            }

            var principals = (await directory.GetAsync(grants.Select(g => g.PrincipalId).ToList(), cancellationToken))
                .ToDictionary(p => p.Id);

            var shares = grants
                .Where(g => principals.ContainsKey(g.PrincipalId))
                .Select(g => new ShareResource
                {
                    PrincipalId = g.PrincipalId,
                    Kind = principals[g.PrincipalId].Kind,
                    DisplayName = principals[g.PrincipalId].DisplayName,
                    Email = principals[g.PrincipalId].Email,
                    Rights = AclRights.Format(g.Rights),
                    Links = { new Link("self", $"/api/{kind}/{collection.Id}/shares/{g.PrincipalId}") },
                })
                .OrderBy(s => s.DisplayName)
                .ToList();

            if (shares.Count > 0)
            {
                items.Add(new SharedByMeResource
                {
                    Id = collection.Id,
                    Kind = kind,
                    Name = collection.Name,
                    Shares = shares,
                    Links = { new Link("shares", $"/api/{kind}/{collection.Id}/shares") },
                });
            }
        }

        return new CollectionResource<SharedByMeResource>
        {
            Items = items.OrderBy(i => i.Name).ToList(),
            Links = { new Link("self", "/api/shared-by-me") },
        };
    }
}
