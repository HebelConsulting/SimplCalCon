using System.IO.Compression;
using System.Text;
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

        // (Uid, Blob) for both paths. NB: BlobText.EnsureVCardUid returns (Blob, Uid) — it must
        // be reordered here, or the loop below (which reads uid-first) would treat the whole
        // vCard as the UID and the UID as the payload, failing every contact import.
        IEnumerable<(string Uid, string Blob)> items = isCalendar
            ? CalendarObjectParser.Split(content).Select(x => (x.Uid, x.Blob))
            : ContactObjectParser.Split(content).Select(block =>
            {
                var (blob, uid) = BlobText.EnsureVCardUid(block);
                return (uid, blob);
            });

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

    public async Task<ImportOutcome> ImportArchiveAsync(
        Guid collectionId,
        byte[] archive,
        ImportConflictMode conflictMode,
        Guid? authorPrincipalId,
        CancellationToken cancellationToken)
    {
        var collection = await dbContext.Collections
            .FirstOrDefaultAsync(c => c.Id == collectionId && !c.IsDeleted, cancellationToken)
            ?? throw new CollectionNotFoundException(collectionId);

        var extension = collection is Calendar ? ".ics" : ".vcf";
        int imported = 0, skipped = 0, failed = 0;
        var errors = new List<string>();

        using var stream = new MemoryStream(archive);
        // An invalid archive throws InvalidDataException — the Api maps it to 400.
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        // Recurse every entry (Google puts the .ics files under a folder); match by file name so
        // directory entries and unrelated files (e.g. a README) are ignored.
        var matching = zip.Entries
            .Where(e => e.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in matching)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var content = await reader.ReadToEndAsync(cancellationToken);

            var outcome = await ImportAsync(collectionId, content, conflictMode, authorPrincipalId, cancellationToken);
            imported += outcome.Imported;
            skipped += outcome.Skipped;
            failed += outcome.Failed;
            errors.AddRange(outcome.Errors);
        }

        if (matching.Count == 0)
        {
            errors.Add($"No {extension} files were found in the archive.");
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
