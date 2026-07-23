using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects.Exceptions;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Builds and ingests migration takeout archives (ADR 0029). Export lists the user's owned
/// collections and serialises each through <see cref="IObjectImportExport"/>; import recreates
/// each manifest collection with a fresh resource name and imports its objects. Per-object and
/// per-collection failures never abort the batch.
/// </summary>
internal sealed class AccountTakeout(IDavRepository repository, IObjectImportExport importExport, IClock clock)
    : IAccountTakeout
{
    private const int ManifestVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<byte[]> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var calendars = await repository.ListCalendarsAsync(userId, cancellationToken);
        var addressBooks = await repository.ListAddressBooksAsync(userId, cancellationToken);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entries = new List<TakeoutCollection>();

            foreach (var calendar in calendars)
            {
                var path = $"calendars/{calendar.ResourceName}.ics";
                await WriteEntryAsync(archive, path, await importExport.ExportAsync(calendar.Id, cancellationToken), cancellationToken);
                entries.Add(new TakeoutCollection(
                    "calendar", calendar.Name, calendar.ResourceName, calendar.SupportsEvents, calendar.SupportsTasks, path));
            }

            foreach (var addressBook in addressBooks)
            {
                var path = $"addressbooks/{addressBook.ResourceName}.vcf";
                await WriteEntryAsync(archive, path, await importExport.ExportAsync(addressBook.Id, cancellationToken), cancellationToken);
                entries.Add(new TakeoutCollection("addressbook", addressBook.Name, addressBook.ResourceName, false, false, path));
            }

            var manifest = new TakeoutManifest(ManifestVersion, clock.UtcNow.UtcDateTime, entries);
            await WriteEntryAsync(archive, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);
        }

        return buffer.ToArray();
    }

    public async Task<TakeoutImportResult> ImportAsync(
        Guid userId, Guid tenantId, byte[] archive, ImportConflictMode conflictMode, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(archive);
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidTakeoutException($"not a readable ZIP archive: {ex.Message}");
        }

        using var _ = zip;
        var manifest = await ReadManifestAsync(zip, cancellationToken)
            ?? throw new InvalidTakeoutException("The archive has no manifest.json.");

        int created = 0, imported = 0, skipped = 0, failed = 0;
        var errors = new List<string>();

        foreach (var entry in manifest.Collections)
        {
            var file = zip.GetEntry(entry.File);
            if (file is null)
            {
                failed++;
                errors.Add($"{entry.Name}: file '{entry.File}' missing from archive.");
                continue;
            }

            // Always create a fresh collection (ADR 0029) so existing ones are never touched.
            var resourceName = UniqueResourceName(entry.Name);
            var collectionId = entry.Type == "addressbook"
                ? (await repository.CreateAddressBookAsync(userId, tenantId, resourceName, entry.Name, cancellationToken)).Id
                : (await repository.CreateCalendarAsync(
                    userId, tenantId, resourceName, entry.Name, entry.SupportsEvents, entry.SupportsTasks, cancellationToken)).Id;
            created++;

            var content = await ReadEntryAsync(file, cancellationToken);
            var outcome = await importExport.ImportAsync(collectionId, content, conflictMode, userId, cancellationToken);
            imported += outcome.Imported;
            skipped += outcome.Skipped;
            failed += outcome.Failed;
            errors.AddRange(outcome.Errors.Select(e => $"{entry.Name}: {e}"));
        }

        return new TakeoutImportResult(created, imported, skipped, failed, errors);
    }

    private static async Task<TakeoutManifest?> ReadManifestAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        var entry = zip.GetEntry("manifest.json");
        if (entry is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TakeoutManifest>(await ReadEntryAsync(entry, cancellationToken), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidTakeoutException($"manifest.json is not valid: {ex.Message}");
        }
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, string content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string UniqueResourceName(string name)
    {
        var slug = new string((name ?? string.Empty).ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return $"{(slug.Length == 0 ? "collection" : slug)}-{Guid.NewGuid():N}";
    }

    private sealed record TakeoutManifest(int Version, DateTime ExportedAtUtc, IReadOnlyList<TakeoutCollection> Collections);

    private sealed record TakeoutCollection(
        string Type, string Name, string ResourceName, bool SupportsEvents, bool SupportsTasks, string File);
}
