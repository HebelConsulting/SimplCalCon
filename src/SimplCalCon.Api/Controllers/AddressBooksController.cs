using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Errors.Exceptions.Resources;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Controllers;

/// <summary>Address books the caller owns or has a grant on (ADR 0009, 0010).</summary>
[Route("api/address-books")]
public sealed class AddressBooksController(
    IDavRepository repository, IObjectImportExport importExport, IAclService acl,
    IUserCollectionColorService colors) : ApiControllerBase(acl)
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<AddressBookResource>>> List(CancellationToken cancellationToken)
    {
        var addressBooks = await repository.ListAccessibleAddressBooksAsync(CurrentUserId, cancellationToken);
        var myColors = await colors.GetOverridesAsync(CurrentUserId, addressBooks.Select(a => a.Id).ToList(), cancellationToken);
        return new CollectionResource<AddressBookResource>
        {
            Items = addressBooks.Select(a => ResourceMapper.MapAddressBook(a, CurrentUserId, myColors.GetValueOrDefault(a.Id))).ToList(),
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
        var myColor = await colors.GetOverrideAsync(CurrentUserId, id, cancellationToken);
        return ResourceMapper.MapAddressBook(addressBook, CurrentUserId, myColor);
    }

    // The caller's personal colour override (ADR 0066): any reader may set/clear their own colour.
    [HttpPut("{id:guid}/color")]
    public async Task<IActionResult> SetColor(Guid id, [FromBody] CollectionColorRequest request, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);
        await colors.SetAsync(CurrentUserId, id, request.Color, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/color")]
    public async Task<IActionResult> ClearColor(Guid id, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);
        await colors.ClearAsync(CurrentUserId, id, cancellationToken);
        return NoContent();
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

    [HttpPut("{id:guid}")]
    [RequireIfMatch]
    public async Task<ActionResult<AddressBookResource>> Update(
        Guid id, [FromBody] CollectionUpdateRequest request, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Address book", id);

        if (addressBook.OwnerId != CurrentUserId)
        {
            throw new InsufficientRightsException();
        }

        EnsureIfMatch(addressBook.ConcurrencyToken);
        var updated = (AddressBook)(await repository.UpdateCollectionAsync(id, request.Name, request.Color, cancellationToken))!;
        return ResourceMapper.MapAddressBook(updated, CurrentUserId);
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

    // --- Import / export (ADR 0013/0029). A bulk write/read is a genuine action, so a verb sub-resource is used. ---

    [HttpPost("{id:guid}/import")]
    public async Task<ActionResult<ImportResultResource>> Import(
        Guid id, IFormFile? file, [FromForm] string? onConflict, [FromForm] bool? separateCollections,
        [FromForm] bool? mergeByName, CancellationToken cancellationToken)
    {
        await RequireRightsAsync(id, AclRight.WriteContent, cancellationToken);
        if (file is null or { Length: 0 })
        {
            return BadRequest("A .vcf or .zip file is required.");
        }

        var bytes = await Portability.ReadBytesAsync(file, cancellationToken);
        try
        {
            // A zip + "separate" recreates each file as its own new address book (ADR 0040).
            if (separateCollections == true && Portability.IsZip(file, bytes))
            {
                if (CurrentTenantId is not { } tenantId)
                {
                    throw new InsufficientRightsException();
                }

                var result = await importExport.ImportArchiveToNewCollectionsAsync(
                    CurrentUserId, tenantId, isCalendar: false, bytes, Portability.Conflict(onConflict),
                    mergeByName != false, cancellationToken);
                return Portability.Map(result);
            }

            var outcome = await Portability.RunImportAsync(importExport, id, file, bytes, onConflict, CurrentUserId, cancellationToken);
            return Portability.Map(outcome);
        }
        catch (System.IO.InvalidDataException)
        {
            return BadRequest("The uploaded file is not a valid zip archive.");
        }
    }

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Address book", id);
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);

        var document = await importExport.ExportAsync(id, cancellationToken);
        return Portability.Download(document, "text/vcard", $"{addressBook.ResourceName}.vcf");
    }

    [HttpHead("{id:guid}/export")]
    public async Task<IActionResult> HeadExport(Guid id, CancellationToken cancellationToken)
    {
        _ = await repository.GetAddressBookByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException("Address book", id);
        await RequireRightsAsync(id, AclRight.Read, cancellationToken);
        return Ok();
    }
}
