using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Read-only subscription feeds (ADR 0069): an unguessable per-collection token in the URL is the
/// only credential (the feed is anonymous). A wrong or absent token → 404 (no existence leak). The
/// feed intentionally bypasses ACL — the owner shares a capability link and can rotate/revoke it.
/// Supports conditional GET (ETag / If-None-Match → 304, ADR 0071) so a polling subscriber skips the
/// export when nothing changed.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class FeedController(IDavRepository repository, IObjectImportExport importExport) : ControllerBase
{
    [HttpGet("api/calendars/{id:guid}/feed/{token}.ics")]
    [HttpHead("api/calendars/{id:guid}/feed/{token}.ics")]
    public async Task<IActionResult> CalendarFeed(Guid id, string token, CancellationToken cancellationToken) =>
        await ServeAsync(await repository.GetCalendarByIdAsync(id, cancellationToken), token, id, "text/calendar; charset=utf-8", cancellationToken);

    [HttpGet("api/address-books/{id:guid}/feed/{token}.vcf")]
    [HttpHead("api/address-books/{id:guid}/feed/{token}.vcf")]
    public async Task<IActionResult> AddressBookFeed(Guid id, string token, CancellationToken cancellationToken) =>
        await ServeAsync(await repository.GetAddressBookByIdAsync(id, cancellationToken), token, id, "text/vcard; charset=utf-8", cancellationToken);

    private async Task<IActionResult> ServeAsync(
        Collection? collection, string providedToken, Guid id, string contentType, CancellationToken cancellationToken)
    {
        if (collection is null || !TokenMatches(collection.FeedToken, providedToken))
        {
            return NotFound();
        }

        // The collection's concurrency token changes on any content change (object writes bump the
        // change sequence) and on rename/recolour, so it's a sound feed ETag.
        var etag = $"\"{collection.ConcurrencyToken:N}\"";
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "private, max-age=0, must-revalidate";

        if (IfNoneMatch(Request.Headers.IfNoneMatch, etag))
        {
            return StatusCode(StatusCodes.Status304NotModified); // unchanged — skip the export
        }

        var document = await importExport.ExportAsync(id, cancellationToken);
        return File(Encoding.UTF8.GetBytes(document), contentType);
    }

    private static bool TokenMatches(string? actual, string provided) =>
        actual is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(provided));

    // Weak comparison per RFC 7232 §3.2: "*" matches, otherwise any listed tag equal to ours (ignoring a W/ prefix).
    private static bool IfNoneMatch(StringValues ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            if (header is null)
            {
                continue;
            }

            foreach (var raw in header.Split(','))
            {
                var tag = raw.Trim();
                if (tag == "*")
                {
                    return true;
                }

                if (tag.StartsWith("W/", StringComparison.Ordinal))
                {
                    tag = tag[2..].Trim();
                }

                if (tag == etag)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
