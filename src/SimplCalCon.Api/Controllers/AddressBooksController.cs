using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Controllers;

/// <summary>Address books the caller owns or has a grant on (ADR 0009, 0010).</summary>
[Route("api/address-books")]
public sealed class AddressBooksController(IDavRepository repository, IAclService acl) : ApiControllerBase(acl)
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<AddressBookResource>>> List(CancellationToken cancellationToken)
    {
        var addressBooks = await repository.ListAccessibleAddressBooksAsync(CurrentUserId, cancellationToken);
        return new CollectionResource<AddressBookResource>
        {
            Items = addressBooks.Select(a => ResourceMapper.MapAddressBook(a, CurrentUserId)).ToList(),
            Links = { new Link("self", "/api/address-books") },
        };
    }

    [HttpHead]
    public IActionResult HeadList() => Ok();

    [HttpGet("{id:guid}", Name = "GetAddressBook")]
    public async Task<ActionResult<AddressBookResource>> Get(Guid id, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Address book", id);
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);
        return ResourceMapper.MapAddressBook(addressBook, CurrentUserId);
    }

    [HttpPost]
    public async Task<ActionResult<AddressBookResource>> Create(
        [FromBody] AddressBookCreateRequest request, CancellationToken cancellationToken)
    {
        if (CurrentTenantId is not { } tenantId)
        {
            throw new InsufficientRightsException();
        }

        var addressBook = await repository.CreateAddressBookAsync(
            CurrentUserId, tenantId, ResourceNames.Slug(request.Name), request.Name, cancellationToken);

        return CreatedAtRoute("GetAddressBook", new { id = addressBook.Id },
            ResourceMapper.MapAddressBook(addressBook, CurrentUserId));
    }

    [HttpDelete("{id:guid}")]
    [RequireIfMatch]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Address book", id);

        if (addressBook.OwnerId != CurrentUserId)
        {
            throw new InsufficientRightsException();
        }

        EnsureIfMatch(addressBook.ConcurrencyToken);
        await repository.DeleteAddressBookAsync(addressBook.OwnerId, addressBook.ResourceName, cancellationToken);
        return NoContent();
    }
}
