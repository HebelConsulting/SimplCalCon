using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Push;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>The addressbook-home-set: lists the user's address books (auto-provisioning a default).</summary>
public sealed class CardDavHomeController(
    IDavRepository repository, IAclService acl, IWebPushConfiguration webPush) : DavControllerBase
{
    [HttpPropfind("~/dav/addressbooks/{userId:guid}")]
    public async Task<IActionResult> Propfind(Guid userId, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));

        await repository.EnsureDefaultAddressBookAsync(userId, CurrentTenantId, cancellationToken);

        var resources = new List<DavResource>
        {
            CardDavResources.Home(HomeHref(userId), PrincipalHref(userId)),
        };

        if (Depth() >= 1)
        {
            // Own collections plus collections shared with the user; a shared collection is
            // rendered at its owner's URL (ADR 0007).
            foreach (var book in await repository.ListAccessibleAddressBooksAsync(userId, cancellationToken))
            {
                var rights = await EffectiveRightsAsync(book, acl, cancellationToken);
                resources.Add(CardDavResources.AddressBookCollection(
                    CollectionHref(book.OwnerId, book.ResourceName), PrincipalHref(book.OwnerId), book, rights,
                    webPush.VapidPublicKey));
            }
        }

        return DavXml.MultiStatus(MultiStatus.Build(request, resources));
    }
}
