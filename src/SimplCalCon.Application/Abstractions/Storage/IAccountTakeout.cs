namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// Account-wide data portability for server-to-server migration (ADR 0013/0029): exports
/// all of a user's owned collections as one self-describing ZIP (a <c>manifest.json</c> +
/// one .ics/.vcf per collection), and ingests such an archive by recreating its
/// collections (always new) and importing their objects.
/// </summary>
public interface IAccountTakeout
{
    Task<byte[]> ExportAsync(Guid userId, CancellationToken cancellationToken);

    Task<TakeoutImportResult> ImportAsync(
        Guid userId, Guid tenantId, byte[] archive, ImportConflictMode conflictMode, CancellationToken cancellationToken);
}

public sealed record TakeoutImportResult(
    int CollectionsCreated, int Imported, int Skipped, int Failed, IReadOnlyList<string> Errors);
