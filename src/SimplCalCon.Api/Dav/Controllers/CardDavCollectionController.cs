using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Collections;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>An address-book collection: PROPFIND listing, REPORT (multiget/query/sync-collection), MKCOL, DELETE.</summary>
public sealed class CardDavCollectionController(IDavRepository repository, IAclService acl) : DavControllerBase
{
    [HttpPropfind("~/dav/addressbooks/{userId:guid}/{book}")]
    public async Task<IActionResult> Propfind(Guid userId, string book, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookAsync(userId, book, cancellationToken);
        if (addressBook is null)
        {
            return NotFound();
        }

        if (!await HasAccessAsync(addressBook, AclRight.Read, acl, cancellationToken))
        {
            return ForbidDav();
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));
        var resources = new List<DavResource>
        {
            CardDavResources.AddressBookCollection(CollectionHref(userId, book), PrincipalHref(userId), addressBook),
        };

        if (Depth() >= 1)
        {
            foreach (var contact in await repository.ListObjectsAsync(addressBook.Id, cancellationToken))
            {
                resources.Add(CardDavResources.ContactObjectResource(
                    ObjectHref(userId, book, contact.ResourceName), contact));
            }
        }

        return DavXml.MultiStatus(MultiStatus.Build(request, resources));
    }

    [HttpReport("~/dav/addressbooks/{userId:guid}/{book}")]
    public async Task<IActionResult> Report(Guid userId, string book, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookAsync(userId, book, cancellationToken);
        if (addressBook is null)
        {
            return NotFound();
        }

        if (!await HasAccessAsync(addressBook, AclRight.Read, acl, cancellationToken))
        {
            return ForbidDav();
        }

        var body = await DavXml.ReadBodyAsync(Request, cancellationToken);
        if (body is null)
        {
            return BadRequest();
        }

        return body.Name switch
        {
            var n when n == DavNames.SyncCollection => await SyncCollectionAsync(userId, book, addressBook, body, cancellationToken),
            var n when n == DavNames.AddressBookMultiget => await MultigetAsync(userId, book, addressBook, body, cancellationToken),
            var n when n == DavNames.AddressBookQuery => await QueryAsync(userId, book, addressBook, body, cancellationToken),
            _ => BadRequest(),
        };
    }

    [HttpMkcol("~/dav/addressbooks/{userId:guid}/{book}")]
    public async Task<IActionResult> Mkcol(Guid userId, string book, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        if (CurrentTenantId is not { } tenantId)
        {
            return Forbid(SimplCalCon.Api.Authentication.DavAuthenticationDefaults.Scheme);
        }

        if (await repository.GetAddressBookAsync(userId, book, cancellationToken) is not null)
        {
            return StatusCode(StatusCodes.Status405MethodNotAllowed);
        }

        var body = await DavXml.ReadBodyAsync(Request, cancellationToken);
        var displayName = body?.Descendants(DavNames.DisplayName).FirstOrDefault()?.Value;

        await repository.CreateAddressBookAsync(userId, tenantId, book, displayName, cancellationToken);
        return StatusCode(StatusCodes.Status201Created);
    }

    [HttpDelete("~/dav/addressbooks/{userId:guid}/{book}")]
    public async Task<IActionResult> Delete(Guid userId, string book, CancellationToken cancellationToken)
    {
        if (RequireOwner(userId) is { } forbid)
        {
            return forbid;
        }

        return await repository.DeleteAddressBookAsync(userId, book, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    private async Task<IActionResult> SyncCollectionAsync(
        Guid userId, string book, AddressBook addressBook, XElement body, CancellationToken cancellationToken)
    {
        var tokenText = body.Element(DavNames.SyncToken)?.Value;
        long? sinceToken;
        if (string.IsNullOrEmpty(tokenText))
        {
            sinceToken = null;
        }
        else if (DavTokens.TryParse(tokenText) is { } parsed)
        {
            sinceToken = parsed;
        }
        else
        {
            return InvalidSyncToken();
        }

        var request = PropRequest.FromProp(body.Element(DavNames.Prop));
        var result = await repository.SyncAsync(addressBook.Id, sinceToken, cancellationToken);

        var changed = result.Changed
            .Select(o => CardDavResources.ContactObjectResource(ObjectHref(userId, book, o.ResourceName), o))
            .ToList();

        var document = MultiStatus.Build(request, changed);
        foreach (var removed in result.RemovedResourceNames)
        {
            document.Root!.Add(new XElement(
                DavNames.Response,
                new XElement(DavNames.Href, ObjectHref(userId, book, removed)),
                new XElement(DavNames.Status, DavNames.NotFound)));
        }

        MultiStatus.WithSyncToken(document, DavTokens.Format(result.Token));
        return DavXml.MultiStatus(document);
    }

    private async Task<IActionResult> MultigetAsync(
        Guid userId, string book, AddressBook addressBook, XElement body, CancellationToken cancellationToken)
    {
        var request = PropRequest.FromProp(body.Element(DavNames.Prop));
        var names = body.Elements(DavNames.Href).Select(h => LastSegment(h.Value)).ToList();
        var found = (await repository.GetObjectsAsync(addressBook.Id, names, cancellationToken))
            .ToDictionary(o => o.ResourceName);

        var resources = new List<DavResource>();
        var document = MultiStatus.Build(request, resources); // start empty, add responses below
        foreach (var name in names)
        {
            if (found.TryGetValue(name, out var contact))
            {
                var built = MultiStatus.Build(request, [
                    CardDavResources.ContactObjectResource(ObjectHref(userId, book, name), contact)]);
                document.Root!.Add(built.Root!.Elements(DavNames.Response));
            }
            else
            {
                document.Root!.Add(new XElement(
                    DavNames.Response,
                    new XElement(DavNames.Href, ObjectHref(userId, book, name)),
                    new XElement(DavNames.Status, DavNames.NotFound)));
            }
        }

        return DavXml.MultiStatus(document);
    }

    private async Task<IActionResult> QueryAsync(
        Guid userId, string book, AddressBook addressBook, XElement body, CancellationToken cancellationToken)
    {
        // v1: return all live objects with the requested props (filter honoured leniently).
        var request = PropRequest.FromProp(body.Element(DavNames.Prop));
        var resources = (await repository.ListObjectsAsync(addressBook.Id, cancellationToken))
            .Select(o => CardDavResources.ContactObjectResource(ObjectHref(userId, book, o.ResourceName), o))
            .ToList();

        return DavXml.MultiStatus(MultiStatus.Build(request, resources));
    }

    private IActionResult InvalidSyncToken()
    {
        var error = new XDocument(new XElement(
            DavNames.Dav + "error",
            new XAttribute(XNamespace.Xmlns + "d", DavNames.Dav.NamespaceName),
            new XElement(DavNames.Dav + "valid-sync-token")));

        return new ContentResult
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentType = "application/xml; charset=utf-8",
            Content = DavXml.Serialize(error),
        };
    }

    private static string LastSegment(string href) => href.TrimEnd('/').Split('/').Last();
}
