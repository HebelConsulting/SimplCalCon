namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// Splits one calendar event into two at a point in time (ADR 0027): the original is
/// truncated to end at the split point and a copy (fresh UID) covers the remainder,
/// both in the same collection. The full blob is preserved on each half — only the
/// start/end move — so description, location, etc. survive (unlike
/// <see cref="IObjectComposer"/>, which rebuilds from structured fields). Splittability
/// is validated by the caller from the object's extracted fields.
/// </summary>
public interface IEventSplitter
{
    Task<SplitEventResult> SplitEventAsync(
        Guid collectionId, string resourceName, DateTime atUtc, Guid? authorPrincipalId, CancellationToken cancellationToken);
}

public sealed record SplitEventResult(StoredObjectResult Original, StoredObjectResult Copy);
