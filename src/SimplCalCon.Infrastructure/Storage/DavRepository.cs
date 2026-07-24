using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

internal sealed class DavRepository(SimplCalConDbContext dbContext, IClock clock) : IDavRepository
{
    public async Task<AddressBook?> EnsureDefaultAddressBookAsync(
        Guid ownerId, Guid? tenantId, CancellationToken cancellationToken)
    {
        // Always ensure a book at the well-known "contacts" resource, even when the user already
        // has other (web-UI-created) address books — native clients want an obvious default to
        // write to (ADR 0021).
        var existing = await dbContext.AddressBooks
            .FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.ResourceName == "contacts" && !a.IsDeleted, cancellationToken);

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

    public async Task<IReadOnlyList<AddressBook>> ListAccessibleAddressBooksAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var principalIds = await PrincipalGraph.GetPrincipalIdsAsync(dbContext, userId, cancellationToken);

        return await dbContext.AddressBooks
            .Where(a => !a.IsDeleted && (a.OwnerId == userId
                || dbContext.AclEntries.Any(e => e.CollectionId == a.Id
                    && principalIds.Contains(e.PrincipalId)
                    && (e.Rights & AclRight.Read) == AclRight.Read)))
            .OrderBy(a => a.ResourceName)
            .ToListAsync(cancellationToken);
    }

    public async Task<AddressBook?> GetAddressBookAsync(
        Guid ownerId, string resourceName, CancellationToken cancellationToken) =>
        await dbContext.AddressBooks
            .FirstOrDefaultAsync(a => a.OwnerId == ownerId && a.ResourceName == resourceName && !a.IsDeleted, cancellationToken);

    public async Task<AddressBook?> GetAddressBookByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.AddressBooks.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted, cancellationToken);

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

    public async Task<ContactObject?> GetContactObjectByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.ContactObjects.FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted, cancellationToken);

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

    public async Task<Calendar?> EnsureDefaultCalendarAsync(
        Guid ownerId, Guid? tenantId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Calendars
            .Where(c => c.OwnerId == ownerId && !c.IsDeleted)
            .OrderBy(c => c.ResourceName)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        return tenantId is { } tenant
            ? await CreateCalendarAsync(ownerId, tenant, "calendar", "Calendar", true, true, cancellationToken)
            : null;
    }

    public async Task<IReadOnlyList<Calendar>> ListCalendarsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await dbContext.Calendars
            .Where(c => c.OwnerId == ownerId && !c.IsDeleted)
            .OrderBy(c => c.ResourceName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Calendar>> ListAccessibleCalendarsAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var principalIds = await PrincipalGraph.GetPrincipalIdsAsync(dbContext, userId, cancellationToken);

        return await dbContext.Calendars
            .Where(c => !c.IsDeleted && (c.OwnerId == userId
                || dbContext.AclEntries.Any(e => e.CollectionId == c.Id
                    && principalIds.Contains(e.PrincipalId)
                    && (e.Rights & AclRight.Read) == AclRight.Read)))
            .OrderBy(c => c.ResourceName)
            .ToListAsync(cancellationToken);
    }

    public async Task<Calendar?> GetCalendarAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken) =>
        await dbContext.Calendars
            .FirstOrDefaultAsync(c => c.OwnerId == ownerId && c.ResourceName == resourceName && !c.IsDeleted, cancellationToken);

    public async Task<Calendar?> GetCalendarByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Calendars.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);

    public async Task<Calendar> CreateCalendarAsync(
        Guid ownerId,
        Guid tenantId,
        string resourceName,
        string? displayName,
        bool supportsEvents,
        bool supportsTasks,
        CancellationToken cancellationToken)
    {
        var calendar = new Calendar
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerId = ownerId,
            Name = string.IsNullOrWhiteSpace(displayName) ? resourceName : displayName,
            ResourceName = resourceName,
            CreatedAt = clock.UtcNow.UtcDateTime,
            SupportsEvents = supportsEvents,
            SupportsTasks = supportsTasks,
        };

        dbContext.Calendars.Add(calendar);
        await dbContext.SaveChangesAsync(cancellationToken);
        return calendar;
    }

    public async Task<bool> DeleteCalendarAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken)
    {
        var calendar = await dbContext.Calendars
            .FirstOrDefaultAsync(c => c.OwnerId == ownerId && c.ResourceName == resourceName && !c.IsDeleted, cancellationToken);

        if (calendar is null)
        {
            return false;
        }

        calendar.IsDeleted = true;
        calendar.DeletedAt = clock.UtcNow.UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CalendarObject>> ListCalendarObjectsAsync(
        Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.CalendarObjects
            .Include(o => o.Attendees)
            .Where(o => o.CollectionId == collectionId && !o.IsDeleted)
            .OrderBy(o => o.ResourceName)
            .ToListAsync(cancellationToken);

    public async Task<CalendarObject?> GetCalendarObjectAsync(
        Guid collectionId, string resourceName, CancellationToken cancellationToken) =>
        await dbContext.CalendarObjects
            .FirstOrDefaultAsync(o => o.CollectionId == collectionId && o.ResourceName == resourceName && !o.IsDeleted, cancellationToken);

    public async Task<CalendarObject?> GetCalendarObjectByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.CalendarObjects
            .Include(o => o.Attendees)
            .FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<CalendarObject>> GetCalendarObjectsAsync(
        Guid collectionId, IReadOnlyCollection<string> resourceNames, CancellationToken cancellationToken) =>
        await dbContext.CalendarObjects
            .Where(o => o.CollectionId == collectionId && !o.IsDeleted && resourceNames.Contains(o.ResourceName))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CalendarObject>> QueryCalendarObjectsAsync(
        Guid collectionId, DateTime? startUtc, DateTime? endUtc, CancellationToken cancellationToken)
    {
        var query = dbContext.CalendarObjects
            .Include(o => o.Attendees)
            .Where(o => o.CollectionId == collectionId && !o.IsDeleted);

        if (startUtc is null && endUtc is null)
        {
            return await query.ToListAsync(cancellationToken);
        }

        // Pre-filter in SQL: non-recurring objects by overlap; recurring and no-start
        // objects are candidates that we expand precisely below.
        query = query.Where(o => o.IsRecurring || o.DtStartUtc == null
            || ((endUtc == null || o.DtStartUtc < endUtc)
                && (startUtc == null || (o.DtEndUtc ?? o.DtStartUtc) >= startUtc)));

        var candidates = await query.ToListAsync(cancellationToken);

        return candidates
            .Where(o => !o.IsRecurring || CalendarOccurrence.OverlapsRange(o.Blob, startUtc, endUtc))
            .ToList();
    }

    public async Task<DavCalendarSyncResult> SyncCalendarAsync(
        Guid collectionId, long? sinceToken, CancellationToken cancellationToken)
    {
        var token = await dbContext.Collections
            .Where(c => c.Id == collectionId)
            .Select(c => c.ChangeSequence)
            .FirstAsync(cancellationToken);

        var changed = await dbContext.CalendarObjects
            .Where(o => o.CollectionId == collectionId && !o.IsDeleted
                && (sinceToken == null || o.ChangeNumber > sinceToken))
            .ToListAsync(cancellationToken);

        var removed = sinceToken is null
            ? []
            : await dbContext.CalendarObjects
                .Where(o => o.CollectionId == collectionId && o.IsDeleted && o.ChangeNumber > sinceToken)
                .Select(o => o.ResourceName)
                .ToListAsync(cancellationToken);

        return new DavCalendarSyncResult(changed, removed, token);
    }

    // --- Trash & version history (ADR 0028) ---

    public async Task<IReadOnlyList<CalendarObject>> ListTrashedCalendarObjectsAsync(
        Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.CalendarObjects
            .Where(o => o.CollectionId == collectionId && o.IsDeleted)
            .OrderByDescending(o => o.DeletedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContactObject>> ListTrashedContactObjectsAsync(
        Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.ContactObjects
            .Where(o => o.CollectionId == collectionId && o.IsDeleted)
            .OrderByDescending(o => o.DeletedAt)
            .ToListAsync(cancellationToken);

    public async Task<CalendarObject?> FindCalendarObjectByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.CalendarObjects.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<ContactObject?> FindContactObjectByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.ContactObjects.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ObjectRevision>> ListObjectRevisionsAsync(
        Guid objectId, CancellationToken cancellationToken) =>
        await dbContext.ObjectRevisions
            .Where(r => r.ObjectId == objectId)
            .OrderByDescending(r => r.RevisionNumber)
            .ToListAsync(cancellationToken);
}
