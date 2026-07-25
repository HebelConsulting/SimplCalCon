using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;

namespace SimplCalCon.Api.Controllers;

/// <summary>Calendars and address books other users have shared with the signed-in user (ADR 0046).</summary>
[Route("api/shared-with-me")]
public sealed class SharedWithMeController(
    IDavRepository repository, IPrincipalDirectory directory, IAclService acl) : ApiControllerBase(acl)
{
    [HttpGet]
    [HttpHead]
    public async Task<ActionResult<CollectionResource<SharedCollectionResource>>> List(CancellationToken cancellationToken)
    {
        var calendars = (await repository.ListAccessibleCalendarsAsync(CurrentUserId, cancellationToken))
            .Where(c => c.OwnerId != CurrentUserId)
            .Select(c => (Kind: "calendars", Collection: (Collection)c));
        var books = (await repository.ListAccessibleAddressBooksAsync(CurrentUserId, cancellationToken))
            .Where(a => a.OwnerId != CurrentUserId)
            .Select(a => (Kind: "address-books", Collection: (Collection)a));

        var shared = calendars.Concat(books).ToList();
        var owners = (await directory.GetAsync(
                shared.Select(s => s.Collection.OwnerId).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        var items = new List<SharedCollectionResource>();
        foreach (var (kind, collection) in shared)
        {
            var rights = await Acl.GetEffectiveRightsAsync(CurrentUserId, collection.Id, cancellationToken);
            items.Add(new SharedCollectionResource
            {
                Id = collection.Id,
                Kind = kind,
                Name = collection.Name,
                OwnerName = owners.TryGetValue(collection.OwnerId, out var owner) ? owner.DisplayName : "Unknown",
                Rights = AclRights.Format(rights),
                Links = { new Link("self", $"/api/{kind}/{collection.Id}") },
            });
        }

        return new CollectionResource<SharedCollectionResource>
        {
            Items = items.OrderBy(i => i.Name).ToList(),
            Links = { new Link("self", "/api/shared-with-me") },
        };
    }
}
