using System.Text;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Dav.Http;
using SimplCalCon.Api.Dav.Xml;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Objects.Exceptions;

namespace SimplCalCon.Api.Dav.Controllers;

/// <summary>A contact resource: GET/PUT/DELETE with ETag conditionals, and PROPFIND (ACL-enforced, ADR 0007).</summary>
public sealed class CardDavObjectController(
    IDavRepository repository, IObjectStore objectStore, IAclService acl) : DavControllerBase
{
    [HttpGet("~/dav/addressbooks/{userId:guid}/{book}/{name}")]
    public async Task<IActionResult> Get(Guid userId, string book, string name, CancellationToken cancellationToken)
    {
        var (addressBook, access) = await ResolveAsync(userId, book, AclRight.Read, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var contact = await repository.GetObjectAsync(addressBook!.Id, name, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        Response.Headers.ETag = ETag.Format(contact.ConcurrencyToken);
        return Content(contact.Blob, "text/vcard; charset=utf-8");
    }

    [HttpPropfind("~/dav/addressbooks/{userId:guid}/{book}/{name}")]
    public async Task<IActionResult> Propfind(Guid userId, string book, string name, CancellationToken cancellationToken)
    {
        var (addressBook, access) = await ResolveAsync(userId, book, AclRight.Read, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var contact = await repository.GetObjectAsync(addressBook!.Id, name, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        var request = PropRequest.Parse(await DavXml.ReadBodyAsync(Request, cancellationToken));
        var resource = CardDavResources.ContactObjectResource(ObjectHref(userId, book, name), contact);
        return DavXml.MultiStatus(MultiStatus.Build(request, [resource]));
    }

    [HttpPut("~/dav/addressbooks/{userId:guid}/{book}/{name}")]
    public async Task<IActionResult> Put(Guid userId, string book, string name, CancellationToken cancellationToken)
    {
        var (addressBook, access) = await ResolveAsync(userId, book, AclRight.WriteContent, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var existing = await repository.GetObjectAsync(addressBook!.Id, name, cancellationToken);
        if (PreconditionFailed(existing?.ConcurrencyToken, existing is not null))
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var blob = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            var result = await objectStore.PutAsync(
                new PutObjectRequest(addressBook.Id, name, blob, CurrentUserId), cancellationToken);
            Response.Headers.ETag = ETag.Format(result.ETag);
            return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent);
        }
        catch (UidConflictException)
        {
            return StatusCode(StatusCodes.Status409Conflict);
        }
        catch (ObjectStoreException)
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }
    }

    [HttpDelete("~/dav/addressbooks/{userId:guid}/{book}/{name}")]
    public async Task<IActionResult> Delete(Guid userId, string book, string name, CancellationToken cancellationToken)
    {
        var (addressBook, access) = await ResolveAsync(userId, book, AclRight.WriteContent, cancellationToken);
        if (access is not null)
        {
            return access;
        }

        var existing = await repository.GetObjectAsync(addressBook!.Id, name, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (PreconditionFailed(existing.ConcurrencyToken, true))
        {
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        await objectStore.DeleteAsync(addressBook.Id, name, CurrentUserId, cancellationToken);
        return NoContent();
    }

    // Resolves the collection (owned by the route principal) and checks the caller's rights;
    // returns a non-null result to short-circuit with 404/403.
    private async Task<(AddressBook? Collection, IActionResult? Result)> ResolveAsync(
        Guid ownerId, string book, AclRight required, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookAsync(ownerId, book, cancellationToken);
        if (addressBook is null)
        {
            return (null, NotFound());
        }

        return await HasAccessAsync(addressBook, required, acl, cancellationToken)
            ? (addressBook, null)
            : (null, ForbidDav());
    }

    private bool PreconditionFailed(Guid? currentToken, bool exists)
    {
        var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
        if (ifNoneMatch == "*" && exists)
        {
            return true;
        }

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(ifMatch))
        {
            return false;
        }

        return currentToken is null
            || !ETag.TryParse(ifMatch, out var token)
            || token != currentToken;
    }
}
