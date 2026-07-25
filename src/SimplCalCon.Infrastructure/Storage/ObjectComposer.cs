using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

internal sealed class ObjectComposer(SimplCalConDbContext dbContext, IObjectStore objectStore, IClock clock)
    : IObjectComposer
{
    public async Task<StoredObjectResult> PutEventAsync(
        Guid collectionId, string? resourceName, EventInput input, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        var uid = await ResolveUidAsync(collectionId, resourceName, cancellationToken);
        var blob = BuildEvent(uid, input);
        return await objectStore.PutAsync(
            new PutObjectRequest(collectionId, resourceName ?? $"{uid}.ics", blob, authorPrincipalId), cancellationToken);
    }

    public async Task<StoredObjectResult> PutContactAsync(
        Guid collectionId, string? resourceName, ContactInput input, Guid? authorPrincipalId, CancellationToken cancellationToken)
    {
        var uid = await ResolveUidAsync(collectionId, resourceName, cancellationToken);
        var blob = BuildContact(uid, input);
        return await objectStore.PutAsync(
            new PutObjectRequest(collectionId, resourceName ?? $"{uid}.vcf", blob, authorPrincipalId), cancellationToken);
    }

    // Preserve the UID of an object being updated (by resource name); mint one for a create.
    private async Task<string> ResolveUidAsync(Guid collectionId, string? resourceName, CancellationToken cancellationToken)
    {
        if (resourceName is null)
        {
            return Guid.NewGuid().ToString();
        }

        var existing = await dbContext.Objects
            .Where(o => o.CollectionId == collectionId && o.ResourceName == resourceName)
            .Select(o => o.Uid)
            .FirstOrDefaultAsync(cancellationToken);

        return existing ?? Guid.NewGuid().ToString();
    }

    private string BuildEvent(string uid, EventInput input)
    {
        var builder = new StringBuilder();
        builder.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//SimplCalCon//EN\r\nBEGIN:VEVENT\r\n");
        builder.Append("UID:").Append(uid).Append("\r\n");
        builder.Append("DTSTAMP:").Append(Timestamp(clock.UtcNow.UtcDateTime)).Append("\r\n");
        builder.Append("SUMMARY:").Append(Escape(input.Summary)).Append("\r\n");

        if (!string.IsNullOrWhiteSpace(input.Location))
        {
            builder.Append("LOCATION:").Append(Escape(input.Location)).Append("\r\n");
        }

        // Organizer + attendees (ADR 0030). An explicit organizer, or default to the first when only attendees are given.
        if (input.Organizer is { } organizer && !string.IsNullOrWhiteSpace(organizer))
        {
            builder.Append("ORGANIZER:").Append(CalAddress(organizer)).Append("\r\n");
        }

        foreach (var attendee in input.Attendees ?? [])
        {
            if (string.IsNullOrWhiteSpace(attendee.Address))
            {
                continue;
            }

            builder.Append("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE");
            if (!string.IsNullOrWhiteSpace(attendee.CommonName))
            {
                builder.Append(";CN=").Append(Escape(attendee.CommonName));
            }

            builder.Append(':').Append(CalAddress(attendee.Address)).Append("\r\n");
        }

        if (input.IsAllDay)
        {
            var end = input.EndUtc ?? input.StartUtc.AddDays(1);
            builder.Append("DTSTART;VALUE=DATE:").Append(input.StartUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
            builder.Append("DTEND;VALUE=DATE:").Append(end.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append("\r\n");
        }
        else
        {
            var end = input.EndUtc ?? input.StartUtc.AddHours(1);
            builder.Append("DTSTART:").Append(Timestamp(input.StartUtc)).Append("\r\n");
            builder.Append("DTEND:").Append(Timestamp(end)).Append("\r\n");
        }

        // Recurrence (ADR 0050): a structured rule is formatted; an unsupported rule is preserved verbatim.
        var rule = input.Recurrence is { } recurrence ? RecurrenceRule.Format(recurrence) : input.RawRecurrenceRule;
        if (!string.IsNullOrWhiteSpace(rule))
        {
            builder.Append("RRULE:").Append(rule.Trim()).Append("\r\n");
        }

        builder.Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return builder.ToString();
    }

    private static string BuildContact(string uid, ContactInput input)
    {
        var builder = new StringBuilder();
        builder.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
        builder.Append("UID:").Append(uid).Append("\r\n");
        builder.Append("FN:").Append(Escape(input.FormattedName ?? BuildFullName(input))).Append("\r\n");
        builder.Append("N:").Append(Escape(input.FamilyName)).Append(';').Append(Escape(input.GivenName)).Append(";;;\r\n");

        if (!string.IsNullOrWhiteSpace(input.Organization))
        {
            builder.Append("ORG:").Append(Escape(input.Organization)).Append("\r\n");
        }

        foreach (var email in input.Emails.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            builder.Append("EMAIL:").Append(Escape(email)).Append("\r\n");
        }

        foreach (var phone in input.Phones.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            builder.Append("TEL:").Append(Escape(phone)).Append("\r\n");
        }

        builder.Append("END:VCARD\r\n");
        return builder.ToString();
    }

    private static string BuildFullName(ContactInput input) =>
        string.Join(' ', new[] { input.GivenName, input.FamilyName }.Where(n => !string.IsNullOrWhiteSpace(n)));

    // A bare email becomes a mailto: CAL-ADDRESS; an address that already has a scheme is kept.
    private static string CalAddress(string address) =>
        address.Contains(':') ? address.Trim() : $"mailto:{address.Trim()}";

    private static string Timestamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // RFC 5545/6350 text escaping for the fields we emit.
    private static string Escape(string? value) => (value ?? string.Empty)
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");
}
