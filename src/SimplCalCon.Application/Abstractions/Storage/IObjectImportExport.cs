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

    /// <summary>Serializes the collection's live objects into one concatenated document.</summary>
    Task<string> ExportAsync(Guid collectionId, CancellationToken cancellationToken);
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
