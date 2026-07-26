namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>Bulk import/export of a collection as a single .ics/.vcf document (ADR 0013).</summary>
public interface IObjectImportExport
{
    Task<ImportOutcome> ImportAsync(
        Guid collectionId,
        string content,
        ImportConflictMode conflictMode,
        Guid? authorPrincipalId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports every matching entry (.ics for a calendar, .vcf for an address book) inside a zip
    /// archive — e.g. a Google Calendar export — into the collection, aggregating the outcomes.
    /// </summary>
    Task<ImportOutcome> ImportArchiveAsync(
        Guid collectionId,
        byte[] archive,
        ImportConflictMode conflictMode,
        Guid? authorPrincipalId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports a zip by creating <b>new</b> collections (a calendar per .ics / an address book per
    /// .vcf), named from each file's <c>X-WR-CALNAME</c> when present else the file name —
    /// recreating the structure of e.g. a Google export. Existing collections are untouched. When
    /// <paramref name="mergeSameName"/> is true, files that resolve to the same name go into one
    /// collection (so a Google export that lists a calendar twice under one X-WR-CALNAME doesn't
    /// produce duplicate collections); otherwise one collection is created per file.
    /// </summary>
    Task<ArchiveImportOutcome> ImportArchiveToNewCollectionsAsync(
        Guid ownerUserId,
        Guid tenantId,
        bool isCalendar,
        byte[] archive,
        ImportConflictMode conflictMode,
        bool mergeSameName,
        CancellationToken cancellationToken);

    /// <summary>Serializes the collection's live objects into one concatenated document.</summary>
    Task<string> ExportAsync(Guid collectionId, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="ExportAsync(System.Guid,System.Threading.CancellationToken)"/>, but when
    /// <paramref name="includeDeletedCollection"/> is true it also exports a soft-deleted collection —
    /// used for the mandatory pre-purge backup (ADR 0078).
    /// </summary>
    Task<string> ExportAsync(Guid collectionId, bool includeDeletedCollection, CancellationToken cancellationToken);
}

public enum ImportConflictMode
{
    /// <summary>Keep the existing object when a UID already exists.</summary>
    Skip,

    /// <summary>Overwrite the existing object with the imported one.</summary>
    Replace,
}

/// <summary>Per-object outcome of an import; never all-or-nothing (ADR 0013).</summary>
public sealed record ImportOutcome(int Imported, int Skipped, int Failed, IReadOnlyList<string> Errors);

/// <summary>Outcome of an archive import that created new collections (ADR 0040).</summary>
public sealed record ArchiveImportOutcome(int CreatedCollections, ImportOutcome Import);
