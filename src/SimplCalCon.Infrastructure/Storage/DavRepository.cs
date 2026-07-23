using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

internal sealed class DavRepository(SimplCalConDbContext dbContext, IClock clock) : IDavRepository
{
    public async Task<AddressBook?> EnsureDefaultAddressBookAsync(
        Guid ownerId, Guid? tenantId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.AddressBooks
            .Where(a => a.OwnerId == ownerId && !a.IsDeleted)
            .OrderBy(a => a.ResourceName)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        return tenantId is { } tenant
            ? await CreateAddressBookAsync(ownerId, tenant, "contacts", "Contacts", cancellationToken)
            : null;
    }

    public async Task<IReadOnlyList<AddressBook>> ListAddressBooksAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await dbContext.AddressBooks
            .Where(a => a.OwnerId == ownerId && !a.IsDeleted)
            .OrderBy(a => a.ResourceName)
            .ToListAsync(cancellationToken);

    public async Task<AddressBook?> GetAddressBookAsync(
        Guid ownerId, string resourceName, CancellationToken cancellationToken) =>
        await dbContext.AddressBooks
            .FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.ResourceName == resourceName && !a.IsDeleted, cancellationToken);

    public async Task<AddressBook> CreateAddressBookAsync(
        Guid ownerId, Guid tenantId, string resourceName, string? displayName, CancellationToken cancellationToken)
    {
        var addressBook = new AddressBook
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerId = ownerId,
            Name = string.IsNullOrWhiteSpace(displayName) ? resourceName : displayName,
            ResourceName = resourceName,
            CreatedAt = clock.UtcNow.UtcDateTime,
        };

        dbContext.AddressBooks.Add(addressBook);
        await dbContext.SaveChangesAsync(cancellationToken);
        return addressBook;
    }

    public async Task<bool> DeleteAddressBookAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken)
    {
        var addressBook = await dbContext.AddressBooks
            .FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.ResourceName == resourceName && !a.IsDeleted, cancellationToken);

        if (addressBook is null)
        {
            return false;
        }

        addressBook.IsDeleted = true;
        addressBook.DeletedAt = clock.UtcNow.UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ContactObject>> ListObjectsAsync(Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.ContactObjects
            .Where(o => o.CollectionId == collectionId && !o.IsDeleted)
            .OrderBy(o => o.ResourceName)
            .ToListAsync(cancellationToken);

    public async Task<ContactObject?> GetObjectAsync(
        Guid collectionId, string resourceName, CancellationToken cancellationToken) =>
        await dbContext.ContactObjects
            .FirstOrDefaultAsync(o => o.CollectionId == collectionId && o.ResourceName == resourceName && !o.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<ContactObject>> GetObjectsAsync(
        Guid collectionId, IReadOnlyCollection<string> resourceNames, CancellationToken cancellationToken) =>
        await dbContext.ContactObjects
            .Where(o => o.CollectionId == collectionId && !o.IsDeleted && resourceNames.Contains(o.ResourceName))
            .ToListAsync(cancellationToken);

    public async Task<DavSyncResult> SyncAsync(Guid collectionId, long? sinceToken, CancellationToken cancellationToken)
    {
        var token = await dbContext.Collections
            .Where(c => c.Id == collectionId)
            .Select(c => c.ChangeSequence)
            .FirstAsync(cancellationToken);

        var changed = await dbContext.ContactObjects
            .Where(o => o.CollectionId == collectionId && !o.IsDeleted
                && (sinceToken == null || o.ChangeNumber > sinceToken))
            .ToListAsync(cancellationToken);

        // On initial sync there are no removals to report.
        var removed = sinceToken is null
            ? []
            : await dbContext.ContactObjects
                .Where(o => o.CollectionId == collectionId && o.IsDeleted && o.ChangeNumber > sinceToken)
                .Select(o => o.ResourceName)
                .ToListAsync(cancellationToken);

        return new DavSyncResult(changed, removed, token);
    }
}
