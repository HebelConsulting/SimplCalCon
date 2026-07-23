using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects.Exceptions;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Bulk import/export over the single write path (ADR 0013). Per-object errors never abort the batch.</summary>
internal sealed class ObjectImportExport(SimplCalConDbContext dbContext, IObjectStore objectStore) : IObjectImportExport
{
    public async Task<ImportOutcome> ImportAsync(
        Guid collectionId,
        string content,
        ImportConflictMode conflictMode,
        Guid? authorPrincipalId,
        CancellationToken cancellationToken)
    {
        var collection = await dbContext.Collections
            .FirstOrDefaultAsync(c => c.Id == collectionId && !c.IsDeleted, cancellationToken)
            ?? throw new CollectionNotFoundException(collectionId);

        var isCalendar = collection is Calendar;
        var items = isCalendar
            ? CalendarObjectParser.Split(content).Select(x => (x.Uid, x.Blob))
            : ContactObjectParser.Split(content).Select(block => BlobText.EnsureVCardUid(block));

        var extension = isCalendar ? "ics" : "vcf";
        int imported = 0, skipped = 0, failed = 0;
        var errors = new List<string>();

        foreach (var (uid, blob) in items)
        {
            try
            {
                if (conflictMode == ImportConflictMode.Skip && await ExistsAsync(collectionId, uid, cancellationToken))
                {
                    skipped++;
                    continue;
                }

                await objectStore.PutAsync(
                    new PutObjectRequest(collectionId, $"{uid}.{extension}", blob, authorPrincipalId), cancellationToken);
                imported++;
            }
            catch (ObjectStoreException ex)
            {
                failed++;
                errors.Add($"{uid}: {ex.Message}");
            }
        }

        return new ImportOutcome(imported, skipped, failed, errors);
    }

    public async Task<string> ExportAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        var collection = await dbContext.Collections
            .FirstOrDefaultAsync(c => c.Id == collectionId && !c.IsDeleted, cancellationToken)
            ?? throw new CollectionNotFoundException(collectionId);

        // Order by resource name (string) — SQLite can't ORDER BY the DateTime columns.
        var blobs = await dbContext.Objects
            .Where(o => o.CollectionId == collectionId && !o.IsDeleted)
            .OrderBy(o => o.ResourceName)
            .Select(o => o.Blob)
            .ToListAsync(cancellationToken);

        return collection is Calendar
            ? CalendarObjectParser.Merge(blobs)
            : string.Join("\r\n", blobs);
    }

    private Task<bool> ExistsAsync(Guid collectionId, string uid, CancellationToken cancellationToken) =>
        dbContext.Objects.AnyAsync(o => o.CollectionId == collectionId && o.Uid == uid && !o.IsDeleted, cancellationToken);
}
