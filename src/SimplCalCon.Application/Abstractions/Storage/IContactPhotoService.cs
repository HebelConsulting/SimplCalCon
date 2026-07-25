namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// Resolves a contact's photo (ADR 0037): inline card photos are decoded; an external PHOTO URL
/// is fetched server-side (SSRF- + byte-guarded) and cached, and on a persistent source failure
/// the cached bytes are embedded back into the card so it becomes self-contained.
/// </summary>
public interface IContactPhotoService
{
    Task<ContactPhotoResult?> GetPhotoAsync(
        Guid collectionId, Guid contactId, Guid? actingUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Proactively re-fetches up to <paramref name="batchSize"/> stale external photo caches (ADR 0057):
    /// refreshes still-live URLs and self-heals dead ones (embeds the cached bytes into the card), so
    /// photos stay fresh without waiting for a view. Returns the number processed.
    /// </summary>
    Task<int> RefreshStaleAsync(int batchSize, CancellationToken cancellationToken);
}

public sealed record ContactPhotoResult(byte[] Bytes, string ContentType);
