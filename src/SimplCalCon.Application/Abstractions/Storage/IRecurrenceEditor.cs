namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// Per-instance recurring-event edits (ADR 0051), tenant-internal, over the object blob:
/// exclude one occurrence (EXDATE), override one occurrence (a RECURRENCE-ID VEVENT), or split
/// the series at an occurrence ("this and following"). All write through <see cref="IObjectStore"/>
/// (revision + ETag + change-sequence bump); the indexed row keeps reflecting the master.
/// </summary>
public interface IRecurrenceEditor
{
    /// <summary>"This occurrence only" delete — adds an EXDATE to the master.</summary>
    Task ExcludeOccurrenceAsync(
        Guid collectionId, string resourceName, DateTime recurrenceIdUtc, Guid? authorPrincipalId, CancellationToken cancellationToken);

    /// <summary>"This occurrence only" edit — adds/replaces a RECURRENCE-ID override VEVENT with the edited fields.</summary>
    Task OverrideOccurrenceAsync(
        Guid collectionId, string resourceName, DateTime recurrenceIdUtc, EventInput input, Guid? authorPrincipalId, CancellationToken cancellationToken);

    /// <summary>"This and following" delete — ends the series just before the occurrence.</summary>
    Task TruncateSeriesAsync(
        Guid collectionId, string resourceName, DateTime recurrenceIdUtc, Guid? authorPrincipalId, CancellationToken cancellationToken);

    /// <summary>"This and following" edit — ends the old series before the occurrence and creates a new series from the edited fields.</summary>
    Task<StoredObjectResult> SplitSeriesAsync(
        Guid collectionId, string resourceName, DateTime recurrenceIdUtc, EventInput newSeriesInput, Guid? authorPrincipalId, CancellationToken cancellationToken);
}
