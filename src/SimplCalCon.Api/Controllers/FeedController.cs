using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Read-only subscription feeds (ADR 0069): an unguessable per-collection token in the URL is the
/// only credential (the feed is anonymous). A wrong or absent token → 404 (no existence leak). The
/// feed intentionally bypasses ACL — the owner shares a capability link and can rotate/revoke it.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class FeedController(IDavRepository repository, IObjectImportExport importExport) : ControllerBase
{
    [HttpGet("api/calendars/{id:guid}/feed/{token}.ics")]
    [HttpHead("api/calendars/{id:guid}/feed/{token}.ics")]
    public async Task<IActionResult> CalendarFeed(Guid id, string token, CancellationToken cancellationToken)
    {
        var calendar = await repository.GetCalendarByIdAsync(id, cancellationToken);
        if (calendar is null || !TokenMatches(calendar.FeedToken, token))
        {
            return NotFound();
        }

        var document = await importExport.ExportAsync(id, cancellationToken);
        return File(Encoding.UTF8.GetBytes(document), "text/calendar; charset=utf-8");
    }

    [HttpGet("api/address-books/{id:guid}/feed/{token}.vcf")]
    [HttpHead("api/address-books/{id:guid}/feed/{token}.vcf")]
    public async Task<IActionResult> AddressBookFeed(Guid id, string token, CancellationToken cancellationToken)
    {
        var addressBook = await repository.GetAddressBookByIdAsync(id, cancellationToken);
        if (addressBook is null || !TokenMatches(addressBook.FeedToken, token))
        {
            return NotFound();
        }

        var document = await importExport.ExportAsync(id, cancellationToken);
        return File(Encoding.UTF8.GetBytes(document), "text/vcard; charset=utf-8");
    }

    private static bool TokenMatches(string? actual, string provided) =>
        actual is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(provided));
}
