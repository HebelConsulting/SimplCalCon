using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;

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

    private async Task<Domain.Objects.ContactObject> FindAsync(Guid addressBookId, Guid id, CancellationToken cancellationToken)
    {
        var contact = await repository.GetContactObjectByIdAsync(id, cancellationToken);
        return contact is not null && contact.CollectionId == addressBookId
            ? contact
            : throw new ResourceNotFoundException("Contact", id);
    }

    private static ContactInput ToInput(ContactWriteRequest request) =>
        new(request.FormattedName, request.FamilyName, request.GivenName, request.Organization, request.Emails, request.Phones);
}
