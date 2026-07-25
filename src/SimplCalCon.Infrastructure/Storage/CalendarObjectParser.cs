using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Objects.Exceptions;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>iCalendar parsing/extraction/splitting via Ical.Net (ADR 0003, 0004).</summary>
internal static class CalendarObjectParser
{
    public static ExtractedCalendarObject Parse(string blob)
    {
        var calendar = Load(blob);
        var recurrenceRule = ExtractRawRrule(blob);

        var calendarEvent = calendar.Events.FirstOrDefault(e => e.RecurrenceIdentifier is null)
            ?? calendar.Events.FirstOrDefault();
        if (calendarEvent is not null)
        {
            return new ExtractedCalendarObject(
                RequireUid(calendarEvent.Uid),
                CalendarComponentType.Event,
                calendarEvent.Summary,
                calendarEvent.Location,
                ToUtc(calendarEvent.DtStart),
                ToUtc(calendarEvent.DtEnd),
                calendarEvent.IsAllDay,
                calendarEvent.RecurrenceRule is not null,
                recurrenceRule,
                ExtractAttendees(calendarEvent.Organizer, calendarEvent.Attendees));
        }

        var todo = calendar.Todos.FirstOrDefault(t => t.RecurrenceIdentifier is null)
            ?? calendar.Todos.FirstOrDefault();
        if (todo is not null)
        {
            return new ExtractedCalendarObject(
                RequireUid(todo.Uid),
                CalendarComponentType.Todo,
                todo.Summary,
                todo.Properties.Get<string>("LOCATION"),
                ToUtc(todo.DtStart),
                ToUtc(todo.Due),
                todo.DtStart is { HasTime: false },
                todo.RecurrenceRule is not null,
                recurrenceRule,
                ExtractAttendees(todo.Organizer, todo.Attendees));
        }

        throw new MalformedObjectException("No VEVENT or VTODO component was found.");
    }

    // The verbatim first RRULE value (without the "RRULE:" prefix), so a rule richer than the web
    // editor can model is preserved exactly on round-trip (ADR 0050). Unfolds RFC 5545 line folds.
    private static string? ExtractRawRrule(string blob)
    {
        var unfolded = blob.Replace("\r\n ", string.Empty).Replace("\r\n\t", string.Empty)
            .Replace("\n ", string.Empty).Replace("\n\t", string.Empty);
        foreach (var line in unfolded.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed["RRULE:".Length..].Trim();
                return value.Length > 0 ? value : null;
            }
        }

        return null;
    }

    /// <summary>
    /// Splits a single VEVENT at <paramref name="atUtc"/> into two blobs: the original
    /// truncated to end at the split point, and a copy (fresh UID) that starts at the
    /// split point and keeps the original end (ADR 0027). Callers validate splittability
    /// (single non-recurring, non-all-day, in-range event) beforehand from the extracted
    /// fields; this method performs only the mechanical blob transform.
    /// </summary>
    public static (string OriginalBlob, string CopyBlob, string CopyUid) SplitEventAt(string blob, DateTime atUtc)
    {
        var at = new CalDateTime(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc));

        // Two independent loads so mutating one half never touches the other.
        var original = Load(blob);
        PrimaryEvent(original).DtEnd = at;

        var copy = Load(blob);
        var copyEvent = PrimaryEvent(copy);
        var copyUid = Guid.NewGuid().ToString();
        copyEvent.Uid = copyUid;
        copyEvent.DtStart = at;

        var serializer = new CalendarSerializer();
        return (
            serializer.SerializeToString(original) ?? string.Empty,
            serializer.SerializeToString(copy) ?? string.Empty,
            copyUid);
    }

    private static Ical.Net.CalendarComponents.CalendarEvent PrimaryEvent(Ical.Net.Calendar calendar) =>
        calendar.Events.FirstOrDefault(e => e.RecurrenceIdentifier is null)
            ?? calendar.Events.FirstOrDefault()
            ?? throw new MalformedObjectException("No VEVENT component was found to split.");

    // --- Per-instance recurrence transforms (ADR 0051). Each returns a fresh blob. ---

    /// <summary>Excludes a single occurrence from the series by adding an EXDATE to the master.</summary>
    public static string ExcludeOccurrence(string blob, DateTime recurrenceIdUtc)
    {
        var calendar = Load(blob);
        var master = PrimaryEvent(calendar);
        master.ExceptionDates.Add(new CalDateTime(DateTime.SpecifyKind(recurrenceIdUtc, DateTimeKind.Utc)));
        return Serialize(calendar);
    }

    /// <summary>Ends the series just before <paramref name="recurrenceIdUtc"/> (this + all following) and drops overrides from there.</summary>
    public static string TruncateSeriesBefore(string blob, DateTime recurrenceIdUtc)
    {
        var calendar = Load(blob);
        var master = PrimaryEvent(calendar);
        if (master.RecurrenceRule is { } rule)
        {
            rule.Count = null;
            rule.Until = new CalDateTime(DateTime.SpecifyKind(recurrenceIdUtc.AddSeconds(-1), DateTimeKind.Utc));
        }

        foreach (var over in calendar.Events
                     .Where(e => e.RecurrenceIdentifier is { } r && r.StartTime.AsUtc >= recurrenceIdUtc)
                     .ToList())
        {
            calendar.Events.Remove(over);
        }

        return Serialize(calendar);
    }

    /// <summary>Adds/replaces a single-occurrence override (a VEVENT with RECURRENCE-ID) carrying the edited fields.</summary>
    public static string SetOccurrenceOverride(
        string blob, DateTime recurrenceIdUtc, DateTime nowUtc,
        string summary, DateTime startUtc, DateTime? endUtc, bool isAllDay, string? location)
    {
        var calendar = Load(blob);
        var master = PrimaryEvent(calendar);

        foreach (var existing in calendar.Events
                     .Where(e => e.RecurrenceIdentifier is { } r && r.StartTime.AsUtc == recurrenceIdUtc)
                     .ToList())
        {
            calendar.Events.Remove(existing);
        }

        var end = endUtc ?? startUtc.AddHours(1);
        var over = new Ical.Net.CalendarComponents.CalendarEvent
        {
            Uid = master.Uid,
            RecurrenceIdentifier = new RecurrenceIdentifier(new CalDateTime(DateTime.SpecifyKind(recurrenceIdUtc, DateTimeKind.Utc))),
            DtStamp = new CalDateTime(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)),
            DtStart = new CalDateTime(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)),
            DtEnd = new CalDateTime(DateTime.SpecifyKind(end, DateTimeKind.Utc)),
            Summary = summary,
            Location = string.IsNullOrWhiteSpace(location) ? null : location,
        };
        calendar.Events.Add(over);
        return Serialize(calendar);
    }

    private static string Serialize(Ical.Net.Calendar calendar) =>
        new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;

    /// <summary>
    /// Expands recurring components into one VEVENT per occurrence starting within [startUtc, endUtc)
    /// for a calendar-data <c>expand</c> REPORT (ADR 0054): each gets a RECURRENCE-ID + concrete
    /// DTSTART/DTEND and loses its RRULE/EXDATE. A malformed blob is returned unchanged.
    /// </summary>
    public static string ExpandForData(string blob, DateTime startUtc, DateTime endUtc)
    {
        Ical.Net.Calendar calendar;
        try
        {
            calendar = Load(blob);
        }
        catch (Exception)
        {
            return blob;
        }

        var from = new CalDateTime(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc));
        var expanded = new Ical.Net.Calendar { ProductId = calendar.ProductId, Version = calendar.Version };

        foreach (var occurrence in calendar.GetOccurrences(from))
        {
            if (occurrence.Period.StartTime?.AsUtc is not { } start)
            {
                continue;
            }

            if (start >= endUtc)
            {
                break; // the stream is ordered by start
            }

            if (occurrence.Source is not Ical.Net.CalendarComponents.CalendarEvent source)
            {
                continue;
            }

            if (source.Copy<Ical.Net.CalendarComponents.CalendarEvent>() is not { } instance)
            {
                continue;
            }

            instance.RecurrenceRule = null;
            instance.ExceptionDates.Clear();
            instance.RecurrenceIdentifier = new RecurrenceIdentifier(new CalDateTime(DateTime.SpecifyKind(start, DateTimeKind.Utc)));
            instance.DtStart = new CalDateTime(DateTime.SpecifyKind(start, DateTimeKind.Utc));
            // EndTime is null on an occurrence; EffectiveEndTime is start+duration, so expanded
            // instances keep the event's real length (ADR 0054/0067) instead of becoming zero-duration.
            instance.DtEnd = occurrence.Period.EffectiveEndTime?.AsUtc is { } end
                ? new CalDateTime(DateTime.SpecifyKind(end, DateTimeKind.Utc))
                : null;
            expanded.Events.Add(instance);
        }

        return Serialize(expanded);
    }

    /// <summary>Splits a multi-object calendar file into one self-contained blob per UID.</summary>
    public static IEnumerable<(string Uid, string Blob)> Split(string content)
    {
        var source = Load(content);
        var byUid = new Dictionary<string, Ical.Net.Calendar>();

        foreach (var calendarEvent in source.Events)
        {
            Bucket(byUid, source, calendarEvent.Uid).Events.Add(calendarEvent);
        }

        foreach (var todo in source.Todos)
        {
            Bucket(byUid, source, todo.Uid).Todos.Add(todo);
        }

        var serializer = new CalendarSerializer();
        foreach (var (uid, calendar) in byUid)
        {
            yield return (uid, serializer.SerializeToString(calendar) ?? string.Empty);
        }
    }

    /// <summary>Merges per-object blobs into a single VCALENDAR document for export.</summary>
    public static string Merge(IEnumerable<string> blobs)
    {
        var merged = new Ical.Net.Calendar();
        foreach (var blob in blobs)
        {
            var calendar = Load(blob);
            foreach (var calendarEvent in calendar.Events)
            {
                merged.Events.Add(calendarEvent);
            }

            foreach (var todo in calendar.Todos)
            {
                merged.Todos.Add(todo);
            }

            foreach (var timeZone in calendar.TimeZones)
            {
                merged.TimeZones.Add(timeZone);
            }
        }

        return new CalendarSerializer().SerializeToString(merged) ?? string.Empty;
    }

    private static Ical.Net.Calendar Bucket(
        Dictionary<string, Ical.Net.Calendar> byUid, Ical.Net.Calendar source, string? uid)
    {
        var key = string.IsNullOrWhiteSpace(uid) ? Guid.NewGuid().ToString() : uid;
        if (!byUid.TryGetValue(key, out var calendar))
        {
            calendar = new Ical.Net.Calendar { ProductId = source.ProductId, Version = source.Version };
            foreach (var timeZone in source.TimeZones)
            {
                calendar.TimeZones.Add(timeZone);
            }

            byUid[key] = calendar;
        }

        return calendar;
    }

    private static Ical.Net.Calendar Load(string blob)
    {
        try
        {
            return Ical.Net.Calendar.Load(blob) ?? throw new MalformedObjectException("Empty calendar.");
        }
        catch (MalformedObjectException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MalformedObjectException(ex.Message);
        }
    }

    private static DateTime? ToUtc(CalDateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string RequireUid(string? uid) =>
        string.IsNullOrWhiteSpace(uid) ? throw new MalformedObjectException("The component has no UID.") : uid;

    // ORGANIZER (modelled as an attendee row with IsOrganizer=true) + ATTENDEEs, for the indexed table (ADR 0030).
    private static IReadOnlyList<ExtractedAttendee> ExtractAttendees(Organizer? organizer, IList<Attendee>? attendees)
    {
        var result = new List<ExtractedAttendee>();

        if (organizer?.Value is { } organizerAddress)
        {
            result.Add(new ExtractedAttendee(
                organizerAddress.ToString(), organizer.CommonName,
                AttendeeRole.Chair, ParticipationStatus.Accepted, IsOrganizer: true));
        }

        foreach (var attendee in attendees ?? [])
        {
            if (attendee.Value is { } address)
            {
                result.Add(new ExtractedAttendee(
                    address.ToString(), attendee.CommonName,
                    MapRole(attendee.Role), MapParticipationStatus(attendee.ParticipationStatus), IsOrganizer: false));
            }
        }

        return result;
    }

    private static AttendeeRole MapRole(string? role) => role?.ToUpperInvariant() switch
    {
        "CHAIR" => AttendeeRole.Chair,
        "OPT-PARTICIPANT" => AttendeeRole.OptionalParticipant,
        "NON-PARTICIPANT" => AttendeeRole.NonParticipant,
        _ => AttendeeRole.RequiredParticipant,
    };

    private static ParticipationStatus MapParticipationStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "ACCEPTED" => ParticipationStatus.Accepted,
        "DECLINED" => ParticipationStatus.Declined,
        "TENTATIVE" => ParticipationStatus.Tentative,
        "DELEGATED" => ParticipationStatus.Delegated,
        _ => ParticipationStatus.NeedsAction,
    };
}
