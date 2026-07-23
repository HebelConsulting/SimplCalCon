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

        var collection = await dbContext.Collections
            .FirstOrDefaultAsync(c => c.Id == request.CollectionId && !c.IsDeleted, cancellationToken)
            ?? throw new CollectionNotFoundException(request.CollectionId);

        var now = clock.UtcNow.UtcDateTime;
        var blob = request.Blob;

        var existing = await dbContext.Objects
            .FirstOrDefaultAsync(o => o.CollectionId == collection.Id && o.ResourceName == request.ResourceName, cancellationToken);

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

            await EnsureUidFreeAsync(collection.Id, extracted.Uid, request.ResourceName, cancellationToken);

            var calendarObject = (CalendarObject?)existing ?? new CalendarObject
            {
                Id = Guid.NewGuid(),
                CollectionId = collection.Id,
                CreatedAt = now,
                Uid = extracted.Uid,
                ResourceName = request.ResourceName,
                Blob = blob,
            };
            calendarObject.Uid = extracted.Uid;
            calendarObject.ComponentType = extracted.Component;
            calendarObject.Summary = extracted.Summary;
            calendarObject.DtStartUtc = extracted.DtStartUtc;
            calendarObject.DtEndUtc = extracted.DtEndUtc;
            calendarObject.IsAllDay = extracted.IsAllDay;
            calendarObject.IsRecurring = extracted.IsRecurring;
            stored = calendarObject;
        }
        else
        {
            string uid;
            (blob, uid) = BlobText.EnsureVCardUid(blob);
            var extracted = ContactObjectParser.Parse(blob, uid);

            await EnsureUidFreeAsync(collection.Id, extracted.Uid, request.ResourceName, cancellationToken);

            var contact = (ContactObject?)existing ?? new ContactObject
            {
                Id = Guid.NewGuid(),
                CollectionId = collection.Id,
                CreatedAt = now,
                Uid = extracted.Uid,
                ResourceName = request.ResourceName,
                Blob = blob,
            };
            contact.Uid = extracted.Uid;
            contact.FormattedName = extracted.FormattedName;
            contact.FamilyName = extracted.FamilyName;
            contact.GivenName = extracted.GivenName;
            contact.Organization = extracted.Organization;
            contact.Emails = extracted.Emails;
            contact.Phones = extracted.Phones;
            stored = contact;
        }

        stored.ResourceName = request.ResourceName;
        stored.Blob = blob;
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
        await AppendRevisionAsync(
            stored, created ? RevisionOperation.Created : RevisionOperation.Updated, request.AuthorPrincipalId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

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
