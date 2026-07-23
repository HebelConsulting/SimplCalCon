using System.Globalization;
using System.Text;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Dav;

/// <summary>Builds a VFREEBUSY document for the free-busy-query REPORT and schedule-outbox reply (ADR 0030).</summary>
internal static class FreeBusyDocument
{
    public static string Build(
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<BusyPeriod> busy,
        string? method = null,
        string? organizer = null,
        string? attendee = null)
    {
        var builder = new StringBuilder();
        builder.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//SimplCalCon//EN\r\n");
        if (method is not null)
        {
            builder.Append("METHOD:").Append(method).Append("\r\n");
        }

        builder.Append("BEGIN:VFREEBUSY\r\n");
        builder.Append("DTSTAMP:").Append(Stamp(fromUtc)).Append("\r\n");
        builder.Append("DTSTART:").Append(Stamp(fromUtc)).Append("\r\n");
        builder.Append("DTEND:").Append(Stamp(toUtc)).Append("\r\n");
        if (organizer is not null)
        {
            builder.Append("ORGANIZER:").Append(organizer).Append("\r\n");
        }

        if (attendee is not null)
        {
            builder.Append("ATTENDEE:").Append(attendee).Append("\r\n");
        }

        if (busy.Count > 0)
        {
            builder.Append("FREEBUSY:")
                .Append(string.Join(',', busy.Select(p => $"{Stamp(p.StartUtc)}/{Stamp(p.EndUtc)}")))
                .Append("\r\n");
        }

        builder.Append("END:VFREEBUSY\r\nEND:VCALENDAR\r\n");
        return builder.ToString();
    }

    private static string Stamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}
