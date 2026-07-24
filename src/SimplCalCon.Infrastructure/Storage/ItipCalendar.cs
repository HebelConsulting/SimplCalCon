using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// iTIP inspection + message building over Ical.Net for RFC 6638 scheduling (ADR 0031).
/// Kept in Infrastructure so the Api/DAV layer never references Ical.Net.
/// </summary>
internal static class ItipCalendar
{
    /// <summary>Extracts the organizer + attendees of the primary VEVENT, or null if it isn't a scheduling object.</summary>
    public static ItipInfo? Inspect(string blob)
    {
        var calendarEvent = LoadEvent(blob);
        if (calendarEvent?.Organizer?.Value is not { } organizer)
        {
            return null;
        }

        var attendees = (calendarEvent.Attendees ?? [])
            .Where(a => a.Value is not null)
            .Select(a => new ItipAttendee(
                a.Value!.ToString(), Email(a.Value!.ToString()), a.CommonName,
                string.IsNullOrEmpty(a.ParticipationStatus) ? "NEEDS-ACTION" : a.ParticipationStatus))
            .ToList();

        return attendees.Count == 0
            ? null
            : new ItipInfo(RequireUid(calendarEvent.Uid), organizer.ToString(), Email(organizer.ToString()), attendees);
    }

    /// <summary>The organizer's object as a METHOD:REQUEST message (the VEVENT unchanged).</summary>
    public static string Request(string blob) => WithMethod(blob, "REQUEST");

    /// <summary>A METHOD:CANCEL message: STATUS:CANCELLED + bumped SEQUENCE.</summary>
    public static string Cancel(string blob)
    {
        var calendar = Load(blob);
        calendar.Method = "CANCEL";
        if (PrimaryEvent(calendar) is { } calendarEvent)
        {
            calendarEvent.Status = "CANCELLED";
            calendarEvent.Sequence += 1;
        }

        return Serialize(calendar);
    }

    /// <summary>A minimal METHOD:REPLY carrying one attendee's PARTSTAT back to the organizer.</summary>
    public static string Reply(string uid, string organizer, string attendee, string partStat, string? commonName)
    {
        var calendar = new Ical.Net.Calendar { ProductId = "-//SimplCalCon//EN", Method = "REPLY" };
        var calendarEvent = new CalendarEvent
        {
            Uid = uid,
            DtStamp = new CalDateTime(System.DateTime.UtcNow),
            Organizer = new Organizer(organizer),
        };
        calendarEvent.Attendees.Add(new Attendee(attendee) { CommonName = commonName, ParticipationStatus = partStat });
        calendar.Events.Add(calendarEvent);
        return Serialize(calendar);
    }

    /// <summary>Sets the PARTSTAT of a matching ATTENDEE on the object; returns the (possibly unchanged) blob.</summary>
    public static string ApplyPartStat(string blob, string attendeeEmail, string partStat)
    {
        var calendar = Load(blob);
        var calendarEvent = PrimaryEvent(calendar);
        var attendee = calendarEvent?.Attendees?.FirstOrDefault(
            a => a.Value is not null && Email(a.Value.ToString()) == attendeeEmail);
        if (attendee is null || attendee.ParticipationStatus == partStat)
        {
            return blob;
        }

        attendee.ParticipationStatus = partStat;
        return Serialize(calendar);
    }

    /// <summary>The lower-cased email of a calendar-user address (strips a <c>mailto:</c> scheme).</summary>
    public static string Email(string address)
    {
        var trimmed = address.Trim();
        var colon = trimmed.IndexOf(':');
        var value = colon >= 0 && trimmed[..colon].Equals("mailto", System.StringComparison.OrdinalIgnoreCase)
            ? trimmed[(colon + 1)..]
            : trimmed;
        return value.ToLowerInvariant();
    }

    private static string WithMethod(string blob, string method)
    {
        var calendar = Load(blob);
        calendar.Method = method;
        return Serialize(calendar);
    }

    private static CalendarEvent? LoadEvent(string blob)
    {
        try
        {
            return PrimaryEvent(Load(blob));
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static Ical.Net.Calendar Load(string blob) => Ical.Net.Calendar.Load(blob) ?? new Ical.Net.Calendar();

    private static CalendarEvent? PrimaryEvent(Ical.Net.Calendar calendar) =>
        calendar.Events.FirstOrDefault(e => e.RecurrenceIdentifier is null) ?? calendar.Events.FirstOrDefault();

    private static string Serialize(Ical.Net.Calendar calendar) =>
        new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;

    private static string RequireUid(string? uid) => string.IsNullOrWhiteSpace(uid) ? System.Guid.NewGuid().ToString() : uid;
}

internal sealed record ItipInfo(string Uid, string Organizer, string OrganizerEmail, IReadOnlyList<ItipAttendee> Attendees);

internal sealed record ItipAttendee(string Address, string Email, string? CommonName, string ParticipationStatus);
