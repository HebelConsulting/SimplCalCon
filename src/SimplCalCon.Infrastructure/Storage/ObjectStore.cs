using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Objects.Exceptions;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// The single write path (ADR 0004): parse → validate → store blob → extract fields →
/// append a revision and bump the collection change sequence, transactionally. The
/// collection's concurrency token serializes concurrent writes to the same collection,
/// keeping the change sequence strictly increasing.
/// </summary>
internal sealed class ObjectStore(SimplCalConDbContext dbContext, IClock clock) : IObjectStore
{
    public async Task<StoredObjectResult> PutAsync(PutObjectRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var collection = await LoadCollectionAsync(request.CollectionId, cancellationToken);
        var now = clock.UtcNow.UtcDateTime;

        var (stored, created) = await MaterializeAsync(
            collection, request.ResourceName, request.Blob, now, cancellationToken);
        var result = await CommitObjectAsync(
            collection, stored, created ? RevisionOperation.Created : RevisionOperation.Updated,
            created, request.AuthorPrincipalId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Brings a trashed object back, or reinstates a prior revision (ADR 0028): re-extracts
    /// from the chosen blob (current tombstone blob, or <paramref name="revisionNumber"/>'s
    /// blob), clears the tombstone, and appends a <see cref="RevisionOperation.Restored"/>
    /// revision with a fresh change number so sync reports the re-appearance. Returns null if
    /// the object (or requested revision) is absent.
    /// </summary>
    public async Task<StoredObjectResult?> RestoreAsync(
        Guid collectionId, string resourceName, long? revisionNumber, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var collection = await LoadCollectionAsync(collectionId, cancellationToken);
        var target = await dbContext.Objects
            .FirstOrDefaultAsync(o => o.CollectionId == collectionId && o.ResourceName == resourceName, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var blob = target.Blob;
        if (revisionNumber is { } number)
        {
            blob = await dbContext.ObjectRevisions
                .Where(r => r.ObjectId == target.Id && r.RevisionNumber == number)
                .Select(r => r.Blob)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new RevisionNotFoundException(target.Id, number);
        }

        var now = clock.UtcNow.UtcDateTime;
        var (stored, created) = await MaterializeAsync(collection, resourceName, blob, now, cancellationToken);
        var result = await CommitObjectAsync(
            collection, stored, RevisionOperation.Restored, created, authorPrincipalId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>Permanently removes one trashed object and its revision history (ADR 0028). Returns false if it isn't in the trash.</summary>
    public async Task<bool> PurgeAsync(Guid collectionId, string resourceName, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var stored = await dbContext.Objects.FirstOrDefaultAsync(
            o => o.CollectionId == collectionId && o.ResourceName == resourceName && o.IsDeleted, cancellationToken);
        if (stored is null)
        {
            return false;
        }

        await dbContext.ObjectRevisions.Where(r => r.ObjectId == stored.Id).ExecuteDeleteAsync(cancellationToken);
        dbContext.Objects.Remove(stored);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>Permanently removes every trashed object (and its revisions) in a collection (ADR 0028). Returns the count purged.</summary>
    public async Task<int> PurgeTrashAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var ids = await dbContext.Objects
            .Where(o => o.CollectionId == collectionId && o.IsDeleted)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return 0;
        }

        await dbContext.ObjectRevisions.Where(r => ids.Contains(r.ObjectId)).ExecuteDeleteAsync(cancellationToken);
        var purged = await dbContext.Objects
            .Where(o => o.CollectionId == collectionId && o.IsDeleted)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purged;
    }

    private async Task<Collection> LoadCollectionAsync(Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.Collections.FirstOrDefaultAsync(c => c.Id == collectionId && !c.IsDeleted, cancellationToken)
            ?? throw new CollectionNotFoundException(collectionId);

    // Parse + extract the blob onto a new-or-existing object row (shared by Put and Restore);
    // revision/change bookkeeping is applied by CommitObjectAsync.
    private async Task<(CollectionObject Stored, bool Created)> MaterializeAsync(
        Collection collection, string resourceName, string blob, DateTime now, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Objects
            .FirstOrDefaultAsync(o => o.CollectionId == collection.Id && o.ResourceName == resourceName, cancellationToken);
        var created = existing is null;
        CollectionObject stored;

        if (collection is Calendar calendar)
        {
            var extracted = CalendarObjectParser.Parse(blob);
            var allowed = extracted.Component == CalendarComponentType.Event
                ? calendar.SupportsEvents
                : calendar.SupportsTasks;
            if (!allowed)
            {
                throw new ComponentNotAllowedException(extracted.Component.ToString());
            }

            await EnsureUidFreeAsync(collection.Id, extracted.Uid, resourceName, cancellationToken);

            var calendarObject = (CalendarObject?)existing ?? new CalendarObject
            {
                Id = Guid.NewGuid(),
                CollectionId = collection.Id,
                CreatedAt = now,
                Uid = extracted.Uid,
                ResourceName = resourceName,
                Blob = blob,
            };
            calendarObject.Uid = extracted.Uid;
            calendarObject.ComponentType = extracted.Component;
            calendarObject.Summary = extracted.Summary;
            calendarObject.DtStartUtc = extracted.DtStartUtc;
            calendarObject.DtEndUtc = extracted.DtEndUtc;
            calendarObject.IsAllDay = extracted.IsAllDay;
            calendarObject.IsRecurring = extracted.IsRecurring;
            calendarObject.Blob = blob;
            await RebuildAttendeesAsync(calendarObject, created, extracted.Attendees, cancellationToken);
            stored = calendarObject;
        }
        else
        {
            var (normalizedBlob, uid) = BlobText.EnsureVCardUid(blob);
            var extracted = ContactObjectParser.Parse(normalizedBlob, uid);

            await EnsureUidFreeAsync(collection.Id, extracted.Uid, resourceName, cancellationToken);

            var contact = (ContactObject?)existing ?? new ContactObject
            {
                Id = Guid.NewGuid(),
                CollectionId = collection.Id,
                CreatedAt = now,
                Uid = extracted.Uid,
                ResourceName = resourceName,
                Blob = normalizedBlob,
            };
            contact.Uid = extracted.Uid;
            contact.FormattedName = extracted.FormattedName;
            contact.FamilyName = extracted.FamilyName;
            contact.GivenName = extracted.GivenName;
            contact.Organization = extracted.Organization;
            contact.Emails = extracted.Emails;
            contact.Phones = extracted.Phones;
            contact.Blob = normalizedBlob;
            stored = contact;
        }

        stored.ResourceName = resourceName;
        return (stored, created);
    }

    // Apply the revision/tombstone/change-sequence bookkeeping and persist (inside the caller's transaction).
    private async Task<StoredObjectResult> CommitObjectAsync(
        Collection collection, CollectionObject stored, RevisionOperation operation, bool created,
        Guid? authorPrincipalId, DateTime now, CancellationToken cancellationToken)
    {
        stored.UpdatedAt = now;
        stored.IsDeleted = false;
        stored.DeletedAt = null;
        stored.RevisionNumber = created ? 1 : stored.RevisionNumber + 1;
        stored.ChangeNumber = ++collection.ChangeSequence;

        if (created)
        {
            dbContext.Objects.Add(stored);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await AppendRevisionAsync(stored, operation, authorPrincipalId, now, cancellationToken);

        return new StoredObjectResult(
            stored.Id, stored.Uid, stored.ResourceName, stored.ConcurrencyToken, stored.RevisionNumber, created);
    }

    public async Task<bool> DeleteAsync(
        Guid collectionId, string resourceName, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var collection = await dbContext.Collections
            .FirstOrDefaultAsync(c => c.Id == collectionId && !c.IsDeleted, cancellationToken)
            ?? throw new CollectionNotFoundException(collectionId);

        var stored = await dbContext.Objects.FirstOrDefaultAsync(
            o => o.CollectionId == collectionId && o.ResourceName == resourceName && !o.IsDeleted, cancellationToken);

        if (stored is null)
        {
            return false;
        }

        var now = clock.UtcNow.UtcDateTime;
        stored.IsDeleted = true;
        stored.DeletedAt = now;
        stored.UpdatedAt = now;
        stored.RevisionNumber += 1;
        stored.ChangeNumber = ++collection.ChangeSequence;

        await dbContext.SaveChangesAsync(cancellationToken);
        await AppendRevisionAsync(stored, RevisionOperation.Deleted, authorPrincipalId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    // Rebuild the indexed attendee rows from the parsed blob (ADR 0030); the blob stays the source of truth.
    private async Task RebuildAttendeesAsync(
        CalendarObject calendarObject, bool created, IReadOnlyList<ExtractedAttendee> attendees, CancellationToken cancellationToken)
    {
        if (!created)
        {
            await dbContext.EventAttendees.Where(a => a.ObjectId == calendarObject.Id).ExecuteDeleteAsync(cancellationToken);
        }

        calendarObject.Attendees.Clear();
        foreach (var attendee in attendees)
        {
            calendarObject.Attendees.Add(new EventAttendee
            {
                Id = Guid.NewGuid(),
                ObjectId = calendarObject.Id,
                Address = attendee.Address,
                NormalizedAddress = attendee.Address.ToUpperInvariant(),
                CommonName = attendee.CommonName,
                Role = attendee.Role,
                ParticipationStatus = attendee.ParticipationStatus,
                IsOrganizer = attendee.IsOrganizer,
            });
        }
    }

    private async Task EnsureUidFreeAsync(
        Guid collectionId, string uid, string resourceName, CancellationToken cancellationToken)
    {
        var conflict = await dbContext.Objects.AnyAsync(
            o => o.CollectionId == collectionId && o.Uid == uid && !o.IsDeleted && o.ResourceName != resourceName,
            cancellationToken);

        if (conflict)
        {
            throw new UidConflictException(uid);
        }
    }

    private async Task AppendRevisionAsync(
        CollectionObject stored, RevisionOperation operation, Guid? author, DateTime now, CancellationToken cancellationToken)
    {
        dbContext.ObjectRevisions.Add(new ObjectRevision
        {
            Id = Guid.NewGuid(),
            ObjectId = stored.Id,
            RevisionNumber = stored.RevisionNumber,
            Blob = stored.Blob,
            ETag = stored.ConcurrencyToken,
            Operation = operation,
            AuthorPrincipalId = author,
            CreatedAt = now,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
