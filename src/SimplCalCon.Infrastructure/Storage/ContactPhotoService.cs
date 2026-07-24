using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Resolves and caches contact photos (ADR 0037). An inline card photo is decoded and returned as
/// is; an external PHOTO URL is fetched server-side (SSRF- + byte-guarded via the "ContactPhotos"
/// <see cref="IHttpClientFactory"/> client), cached in <see cref="ContactPhoto"/>, and served from
/// cache while it is fresh. When the source later fails but a cached copy exists, the cached bytes
/// are embedded back into the card so it becomes self-contained and no longer needs the URL.
/// </summary>
internal sealed class ContactPhotoService(
    SimplCalConDbContext dbContext,
    IObjectStore objectStore,
    IHttpClientFactory httpClientFactory,
    IClock clock,
    ILogger<ContactPhotoService> logger) : IContactPhotoService
{
    public const string HttpClientName = "ContactPhotos";
    private static readonly TimeSpan RevalidateAfter = TimeSpan.FromDays(7);
    private const int MaxPhotoBytes = 5 * 1024 * 1024;

    public async Task<ContactPhotoResult?> GetPhotoAsync(
        Guid collectionId, Guid contactId, Guid? actingUserId, CancellationToken cancellationToken)
    {
        var contact = await dbContext.ContactObjects.AsNoTracking().FirstOrDefaultAsync(
            o => o.Id == contactId && o.CollectionId == collectionId && !o.IsDeleted, cancellationToken);
        if (contact is null)
        {
            return null;
        }

        return VCardPhotoRef.Parse(contact.Blob) switch
        {
            VCardPhotoRef.Inline inline => new ContactPhotoResult(inline.Bytes, inline.ContentType),
            VCardPhotoRef.Url url => await ResolveUrlAsync(contact, url.Value, actingUserId, cancellationToken),
            _ => null,
        };
    }

    private async Task<ContactPhotoResult?> ResolveUrlAsync(
        ContactObject contact, string url, Guid? actingUserId, CancellationToken cancellationToken)
    {
        var cache = await dbContext.ContactPhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ObjectId == contact.Id, cancellationToken);

        var now = clock.UtcNow.UtcDateTime;
        var isFresh = cache is not null
            && cache.SourceUrl == url
            && now - cache.FetchedAt < RevalidateAfter;
        if (isFresh)
        {
            return new ContactPhotoResult(cache!.Photo, cache.ContentType);
        }

        var fetched = await TryFetchAsync(url, cancellationToken);
        if (fetched is { } photo)
        {
            await UpsertCacheAsync(contact, url, photo.Bytes, photo.ContentType, cache is not null, cancellationToken);
            return new ContactPhotoResult(photo.Bytes, photo.ContentType);
        }

        // The source failed. Fall back to the cached copy and make the card self-contained so the
        // photo survives the dead URL from here on.
        if (cache is not null)
        {
            await EmbedIntoCardAsync(contact, cache, actingUserId, cancellationToken);
            return new ContactPhotoResult(cache.Photo, cache.ContentType);
        }

        return null;
    }

    private async Task<(byte[] Bytes, string ContentType)?> TryFetchAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("Contact photo at {Url} was not an image ({ContentType}); ignoring.", uri, contentType);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0 || bytes.Length > MaxPhotoBytes)
            {
                return null;
            }

            return (bytes, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Fetching contact photo from {Url} failed.", url);
            return null;
        }
    }

    private async Task UpsertCacheAsync(
        ContactObject contact, string url, byte[] bytes, string contentType, bool exists, CancellationToken cancellationToken)
    {
        if (exists)
        {
            await dbContext.ContactPhotos.Where(p => p.ObjectId == contact.Id).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.Photo, bytes)
                    .SetProperty(p => p.ContentType, contentType)
                    .SetProperty(p => p.SourceUrl, url)
                    .SetProperty(p => p.FetchedAt, clock.UtcNow.UtcDateTime),
                cancellationToken);
            return;
        }

        var tenantId = await dbContext.Collections.Where(c => c.Id == contact.CollectionId)
            .Select(c => (Guid?)c.TenantId).FirstOrDefaultAsync(cancellationToken);

        dbContext.ContactPhotos.Add(new ContactPhoto
        {
            ObjectId = contact.Id,
            TenantId = tenantId,
            Photo = bytes,
            ContentType = contentType,
            SourceUrl = url,
            FetchedAt = clock.UtcNow.UtcDateTime,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EmbedIntoCardAsync(
        ContactObject contact, ContactPhoto cache, Guid? actingUserId, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Contact photo source for {ResourceName} is unreachable; embedding the cached copy into the card.",
            contact.ResourceName);

        var newBlob = VCardPhotoRef.ReplacePhoto(contact.Blob, cache.Photo, cache.ContentType);
        await objectStore.PutAsync(
            new PutObjectRequest(contact.CollectionId, contact.ResourceName, newBlob, actingUserId), cancellationToken);

        // The card now carries the photo inline, so the cache row is redundant.
        await dbContext.ContactPhotos.Where(p => p.ObjectId == contact.Id).ExecuteDeleteAsync(cancellationToken);
    }
}
