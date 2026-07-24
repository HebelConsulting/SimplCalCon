using System.Text;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Objects.Exceptions;

namespace SimplCalCon.Api.Controllers;

/// <summary>Contacts in an address book. Reads need `read`; writes need `write-content` (ADR 0007, 0009).</summary>
[Route("api/address-books/{addressBookId:guid}/contacts")]
public sealed class ContactsController(
    IDavRepository repository, IObjectStore objectStore, IObjectComposer composer, IAclService acl)
    : ApiControllerBase(acl)
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<ContactResource>>> List(
        Guid addressBookId, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.Read, cancellationToken);
        var contacts = await repository.ListObjectsAsync(addressBookId, cancellationToken);
        return new CollectionResource<ContactResource>
        {
            Items = contacts.Select(ResourceMapper.MapContact).ToList(),
            Links = { new Link("self", $"/api/address-books/{addressBookId}/contacts") },
        };
    }

    [HttpGet("{id:guid}", Name = "GetContact")]
    public async Task<ActionResult<ContactResource>> Get(Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.Read, cancellationToken);
        return ResourceMapper.MapContact(await FindAsync(addressBookId, id, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ContactResource>> Create(
        Guid addressBookId, [FromBody] ContactWriteRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.WriteContent, cancellationToken);
        var result = await composer.PutContactAsync(addressBookId, null, ToInput(request), CurrentUserId, cancellationToken);
        var created = await repository.GetContactObjectByIdAsync(result.Id, cancellationToken);
        return CreatedAtRoute("GetContact", new { addressBookId, id = result.Id }, ResourceMapper.MapContact(created!));
    }

    [HttpPut("{id:guid}")]
    [RequireIfMatch]
    public async Task<ActionResult<ContactResource>> Update(
        Guid addressBookId, Guid id, [FromBody] ContactWriteRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(addressBookId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        await composer.PutContactAsync(addressBookId, existing.ResourceName, ToInput(request), CurrentUserId, cancellationToken);
        var updated = await repository.GetContactObjectByIdAsync(id, cancellationToken);
        return ResourceMapper.MapContact(updated!);
    }

    // Raw vCard read/write (ADR 0036) — lets the UI show and edit the card verbatim. The PUT
    // goes through the same validate-and-extract write path as any object write.
    [HttpGet("{id:guid}/raw")]
    public async Task<IActionResult> GetRaw(Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.Read, cancellationToken);
        var contact = await FindAsync(addressBookId, id, cancellationToken);
        Response.Headers.ETag = ETag.Format(contact.ConcurrencyToken);
        return Content(contact.Blob, "text/vcard; charset=utf-8");
    }

    [HttpPut("{id:guid}/raw")]
    [RequireIfMatch]
    public async Task<IActionResult> PutRaw(Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(addressBookId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var blob = await reader.ReadToEndAsync(cancellationToken);

        try
        {
            var result = await objectStore.PutAsync(
                new PutObjectRequest(addressBookId, existing.ResourceName, blob, CurrentUserId), cancellationToken);
            Response.Headers.ETag = ETag.Format(result.ETag);
            return NoContent();
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

    [HttpDelete("{id:guid}")]
    [RequireIfMatch]
    public async Task<IActionResult> Delete(Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.WriteContent, cancellationToken);
        var existing = await FindAsync(addressBookId, id, cancellationToken);
        EnsureIfMatch(existing.ConcurrencyToken);

        await objectStore.DeleteAsync(addressBookId, existing.ResourceName, CurrentUserId, cancellationToken);
        return NoContent();
    }

    // --- Trash & version history (ADR 0028). Trash/restore act on already-deleted items, so they are If-Match-exempt. ---

    [HttpGet("trash")]
    public async Task<ActionResult<CollectionResource<ContactResource>>> ListTrash(
        Guid addressBookId, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.Read, cancellationToken);
        var trashed = await repository.ListTrashedContactObjectsAsync(addressBookId, cancellationToken);
        return new CollectionResource<ContactResource>
        {
            Items = trashed.Select(ResourceMapper.MapContact).ToList(),
            Links = { new Link("self", $"/api/address-books/{addressBookId}/contacts/trash") },
        };
    }

    [HttpDelete("trash")]
    public async Task<IActionResult> EmptyTrash(Guid addressBookId, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.WriteContent, cancellationToken);
        await objectStore.PurgeTrashAsync(addressBookId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("trash/{id:guid}")]
    public async Task<IActionResult> Purge(Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.WriteContent, cancellationToken);
        var trashed = await ResolveTrashedAsync(addressBookId, id, cancellationToken);
        await objectStore.PurgeAsync(addressBookId, trashed.ResourceName, cancellationToken);
        return NoContent();
    }

    [HttpPost("trash/{id:guid}/restore")]
    public async Task<ActionResult<ContactResource>> Restore(Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.WriteContent, cancellationToken);
        var trashed = await ResolveTrashedAsync(addressBookId, id, cancellationToken);
        var result = await objectStore.RestoreAsync(addressBookId, trashed.ResourceName, null, CurrentUserId, cancellationToken);
        var restored = await repository.GetContactObjectByIdAsync(result!.Id, cancellationToken);
        return ResourceMapper.MapContact(restored!);
    }

    [HttpGet("{id:guid}/revisions")]
    public async Task<ActionResult<CollectionResource<RevisionResource>>> Revisions(
        Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.Read, cancellationToken);
        var contact = await ResolveAnyAsync(addressBookId, id, cancellationToken);
        var revisions = await repository.ListObjectRevisionsAsync(contact.Id, cancellationToken);
        var selfBase = $"/api/address-books/{addressBookId}/contacts/{id}";
        return new CollectionResource<RevisionResource>
        {
            Items = revisions.Select(r => ResourceMapper.MapRevision(r, selfBase)).ToList(),
            Links = { new Link("self", $"{selfBase}/revisions") },
        };
    }

    [HttpPost("{id:guid}/revisions/{number:long}/restore")]
    public async Task<ActionResult<ContactResource>> RestoreRevision(
        Guid addressBookId, Guid id, long number, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(addressBookId, AclRight.WriteContent, cancellationToken);
        var contact = await ResolveAnyAsync(addressBookId, id, cancellationToken);
        var result = await objectStore.RestoreAsync(addressBookId, contact.ResourceName, number, CurrentUserId, cancellationToken);
        var restored = await repository.GetContactObjectByIdAsync(result!.Id, cancellationToken);
        return ResourceMapper.MapContact(restored!);
    }

    private async Task<Domain.Objects.ContactObject> FindAsync(Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        var contact = await repository.GetContactObjectByIdAsync(id, cancellationToken);
        return contact is not null && contact.CollectionId == addressBookId
            ? contact
            : throw new ResourceNotFoundException("Contact", id);
    }

    private async Task<Domain.Objects.ContactObject> ResolveTrashedAsync(
        Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        var found = await repository.FindContactObjectByIdAsync(id, cancellationToken);
        return found is { IsDeleted: true } && found.CollectionId == addressBookId
            ? found
            : throw new ResourceNotFoundException("Trashed contact", id);
    }

    private async Task<Domain.Objects.ContactObject> ResolveAnyAsync(
        Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        var found = await repository.FindContactObjectByIdAsync(id, cancellationToken);
        return found is not null && found.CollectionId == addressBookId
            ? found
            : throw new ResourceNotFoundException("Contact", id);
    }

    private static ContactInput ToInput(ContactWriteRequest request) =>
        new(request.FormattedName, request.FamilyName, request.GivenName, request.Organization, request.Emails, request.Phones);
}
