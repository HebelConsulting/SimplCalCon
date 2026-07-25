using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Objects.Exceptions;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Per-instance recurring-event edits (ADR 0051): loads the object blob, applies the RFC 5545
/// EXDATE / RECURRENCE-ID / UNTIL transform (<see cref="CalendarObjectParser"/>), and stores it
/// back through <see cref="IObjectStore"/> so each edit gets a revision, ETag, and change-sequence
/// bump. The blob stays the source of truth; the indexed row keeps reflecting the master.
/// </summary>
internal sealed class RecurrenceEditor(
    SimplCalConDbContext dbContext, IObjectStore objectStore, IObjectComposer composer, IClock clock)
    : IRecurrenceEditor
{
    public async Task ExcludeOccurrenceAsync(
        Guid collectionId, string resourceName, DateTime recurrenceIdUtc, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        var blob = await LoadBlobAsync(collectionId, resourceName, cancellationToken);
        var updated = CalendarObjectParser.ExcludeOccurrence(blob, recurrenceIdUtc);
        await objectStore.PutAsync(new PutObjectRequest(collectionId, resourceName, updated, authorPrincipalId), cancellationToken);
    }

    public async Task OverrideOccurrenceAsync(
        Guid collectionId, string resourceName, DateTime recurrenceIdUtc, EventInput input, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        var blob = await LoadBlobAsync(collectionId, resourceName, cancellationToken);
        var updated = CalendarObjectParser.SetOccurrenceOverride(
            blob, recurrenceIdUtc, clock.UtcNow.UtcDateTime,
            input.Summary, input.StartUtc, input.EndUtc, input.IsAllDay, input.Location);
        await objectStore.PutAsync(new PutObjectRequest(collectionId, resourceName, updated, authorPrincipalId), cancellationToken);
    }

    public async Task TruncateSeriesAsync(
        Guid collectionId, string resourceName, DateTime recurrenceIdUtc, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        var blob = await LoadBlobAsync(collectionId, resourceName, cancellationToken);
        var updated = CalendarObjectParser.TruncateSeriesBefore(blob, recurrenceIdUtc);
        await objectStore.PutAsync(new PutObjectRequest(collectionId, resourceName, updated, authorPrincipalId), cancellationToken);
    }

    public async Task<StoredObjectResult> SplitSeriesAsync(
        Guid collectionId, string resourceName, DateTime recurrenceIdUtc, EventInput newSeriesInput, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        // End the old series just before the occurrence, then start a new series from the edited fields.
        var blob = await LoadBlobAsync(collectionId, resourceName, cancellationToken);
        var truncated = CalendarObjectParser.TruncateSeriesBefore(blob, recurrenceIdUtc);
        await objectStore.PutAsync(new PutObjectRequest(collectionId, resourceName, truncated, authorPrincipalId), cancellationToken);

        return await composer.PutEventAsync(collectionId, null, newSeriesInput, authorPrincipalId, cancellationToken);
    }

    private async Task<string> LoadBlobAsync(Guid collectionId, string resourceName, CancellationToken cancellationToken) =>
        await dbContext.Objects
            .Where(o => o.CollectionId == collectionId && o.ResourceName == resourceName && !o.IsDeleted)
            .Select(o => o.Blob)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new MalformedObjectException("The recurring event no longer exists.");
}
