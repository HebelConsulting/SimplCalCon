using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects.Exceptions;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Bulk import/export over the single write path (ADR 0013). Per-object errors never abort the batch.</summary>
internal sealed class ObjectImportExport(
    SimplCalConDbContext dbContext, IObjectStore objectStore, IDavRepository repository) : IObjectImportExport
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

    public async Task<ArchiveImportOutcome> ImportArchiveToNewCollectionsAsync(
        Guid ownerUserId,
        Guid tenantId,
        bool isCalendar,
        byte[] archive,
        ImportConflictMode conflictMode,
        bool mergeSameName,
        CancellationToken cancellationToken)
    {
        var extension = isCalendar ? ".ics" : ".vcf";
        int created = 0, imported = 0, skipped = 0, failed = 0;
        var errors = new List<string>();
        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        using var stream = new MemoryStream(archive);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            string content;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
            {
                content = await reader.ReadToEndAsync(cancellationToken);
            }

            // Prefer the calendar's own display name (Google/Apple set X-WR-CALNAME); else the file name.
            var name = (isCalendar ? ExtractCalendarName(content) : null)
                ?? System.IO.Path.GetFileNameWithoutExtension(entry.Name);

            // Reuse the collection already created for this name, or create a fresh one.
            if (!mergeSameName || !byName.TryGetValue(name, out var collectionId))
            {
                var resourceName = UniqueResourceName(name);
                collectionId = isCalendar
                    ? (await repository.CreateCalendarAsync(ownerUserId, tenantId, resourceName, name, true, true, cancellationToken)).Id
                    : (await repository.CreateAddressBookAsync(ownerUserId, tenantId, resourceName, name, cancellationToken)).Id;
                created++;
                if (mergeSameName)
                {
                    byName[name] = collectionId;
                }
            }

            var outcome = await ImportAsync(collectionId, content, conflictMode, ownerUserId, cancellationToken);
            imported += outcome.Imported;
            skipped += outcome.Skipped;
            failed += outcome.Failed;
            errors.AddRange(outcome.Errors.Select(e => $"{name}: {e}"));
        }

        return new ArchiveImportOutcome(created, new ImportOutcome(imported, skipped, failed, errors));
    }

    public Task<string> ExportAsync(Guid collectionId, CancellationToken cancellationToken) =>
        ExportAsync(collectionId, includeDeletedCollection: false, cancellationToken);

    public async Task<string> ExportAsync(Guid collectionId, bool includeDeletedCollection, CancellationToken cancellationToken)
    {
        var collection = await dbContext.Collections
            .FirstOrDefaultAsync(c => c.Id == collectionId && (includeDeletedCollection || !c.IsDeleted), cancellationToken)
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

    // The iCalendar X-WR-CALNAME property carries the calendar's display name (set by Google/Apple).
    private static string? ExtractCalendarName(string content)
    {
        foreach (var line in Unfold(content))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var propertyName = line[..colon];
            var semicolon = propertyName.IndexOf(';');
            if (semicolon >= 0)
            {
                propertyName = propertyName[..semicolon];
            }

            if (propertyName.Equals("X-WR-CALNAME", StringComparison.OrdinalIgnoreCase))
            {
                var value = line[(colon + 1)..].Trim();
                return value.Length > 0 ? value : null;
            }
        }

        return null;
    }

    // RFC 5545 line unfolding: a line starting with a space/tab continues the previous one. Lazy,
    // so a caller scanning for one property near the top doesn't process the whole document.
    private static IEnumerable<string> Unfold(string content)
    {
        var raw = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var current = new StringBuilder();
        var has = false;
        foreach (var line in raw)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && has)
            {
                current.Append(line[1..]);
            }
            else
            {
                if (has)
                {
                    yield return current.ToString();
                }

                current.Clear();
                current.Append(line);
                has = true;
            }
        }

        if (has)
        {
            yield return current.ToString();
        }
    }

    // A URL-safe slug plus a GUID suffix so two files with the same name never collide.
    private static string UniqueResourceName(string name)
    {
        var slug = new string((name ?? string.Empty).ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return $"{(slug.Length == 0 ? "collection" : slug)}-{Guid.NewGuid():N}";
    }
}
