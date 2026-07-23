using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Objects.Exceptions;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Event-split write path (ADR 0027). Loads the object blob, produces the truncated
/// original + a tail copy via <see cref="CalendarObjectParser.SplitEventAt"/>, then
/// stores both through <see cref="IObjectStore"/> so each half gets a revision, ETag,
/// and change-sequence bump. The copy is created first so a failure between the two
/// writes leaves a momentary overlap rather than losing the tail.
/// </summary>
internal sealed class EventSplitter(SimplCalConDbContext dbContext, IObjectStore objectStore) : IEventSplitter
{
    public async Task<SplitEventResult> SplitEventAsync(
        Guid collectionId, string resourceName, DateTime atUtc, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        var blob = await dbContext.Objects
            .Where(o => o.CollectionId == collectionId && o.ResourceName == resourceName && !o.IsDeleted)
            .Select(o => o.Blob)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new MalformedObjectException("The event to split no longer exists.");

        var (originalBlob, copyBlob, copyUid) = CalendarObjectParser.SplitEventAt(blob, atUtc);

        var copy = await objectStore.PutAsync(
            new PutObjectRequest(collectionId, $"{copyUid}.ics", copyBlob, authorPrincipalId), cancellationToken);
        var original = await objectStore.PutAsync(
            new PutObjectRequest(collectionId, resourceName, originalBlob, authorPrincipalId), cancellationToken);

        return new SplitEventResult(original, copy);
    }
}
