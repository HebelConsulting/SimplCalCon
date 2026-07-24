using SimplCalCon.Api.Hypermedia;

namespace SimplCalCon.Api.Contracts;

/// <summary>Outcome of a bulk import into one collection (ADR 0013/0029).</summary>
public sealed class ImportResultResource : HypermediaResource
{
    public required int Imported { get; init; }

    public required int Skipped { get; init; }

    public required int Failed { get; init; }

    /// <summary>New collections created by a per-file archive import (ADR 0040); 0 for a normal import.</summary>
    public int CreatedCollections { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Outcome of ingesting a takeout archive (ADR 0029).</summary>
public sealed class TakeoutImportResource : HypermediaResource
{
    public required int CollectionsCreated { get; init; }

    public required int Imported { get; init; }

    public required int Skipped { get; init; }

    public required int Failed { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];
}
