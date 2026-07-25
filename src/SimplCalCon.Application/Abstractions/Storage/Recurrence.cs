using System.Globalization;

namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// A structured recurrence the web editor can model (ADR 0050): the four simple frequencies with
/// an interval, an optional weekly by-weekday set, and an end (never / until a date / after N).
/// Rules richer than this (BYSETPOS, BYMONTHDAY, ordinal BYDAY, …) are round-tripped as a raw
/// <c>RRULE</c> string instead and shown read-only.
/// </summary>
public sealed record Recurrence(
    string Frequency,
    int Interval,
    IReadOnlyList<string> ByDay,
    int? Count,
    DateTime? UntilUtc);

/// <summary>
/// Parses/formats the <c>RRULE</c> value (without the <c>RRULE:</c> prefix) to and from the
/// structured <see cref="Recurrence"/>. <see cref="TryParse"/> returns false for any rule outside
/// the supported subset, so callers can fall back to preserving the raw string verbatim.
/// </summary>
public static class RecurrenceRule
{
    private static readonly string[] Frequencies = ["DAILY", "WEEKLY", "MONTHLY", "YEARLY"];
    private static readonly string[] Weekdays = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    public static bool TryParse(string? rule, out Recurrence recurrence)
    {
        recurrence = null!;
        if (string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        string? frequency = null;
        var interval = 1;
        var byDay = new List<string>();
        int? count = null;
        DateTime? until = null;

        foreach (var part in rule.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                return false;
            }

            var key = pair[0].ToUpperInvariant();
            var value = pair[1].ToUpperInvariant();
            switch (key)
            {
                case "FREQ":
                    if (!Frequencies.Contains(value))
                    {
                        return false;
                    }

                    frequency = value;
                    break;

                case "INTERVAL":
                    if (!int.TryParse(value, out interval) || interval < 1)
                    {
                        return false;
                    }

                    break;

                case "BYDAY":
                    foreach (var day in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // Only plain weekday codes — an ordinal prefix (e.g. 2MO) is beyond the editor.
                        if (!Weekdays.Contains(day))
                        {
                            return false;
                        }

                        byDay.Add(day);
                    }

                    break;

                case "COUNT":
                    if (!int.TryParse(value, out var parsedCount) || parsedCount < 1)
                    {
                        return false;
                    }

                    count = parsedCount;
                    break;

                case "UNTIL":
                    if (!TryParseUntil(value, out var parsedUntil))
                    {
                        return false;
                    }

                    until = parsedUntil;
                    break;

                default:
                    // Any other part (BYSETPOS, BYMONTHDAY, WKST, …) is outside the supported subset.
                    return false;
            }
        }

        // BYDAY is only modelled for weekly; COUNT and UNTIL are mutually exclusive.
        if (frequency is null || (byDay.Count > 0 && frequency != "WEEKLY") || (count is not null && until is not null))
        {
            return false;
        }

        recurrence = new Recurrence(frequency, interval, byDay, count, until);
        return true;
    }

    public static string Format(Recurrence recurrence)
    {
        var parts = new List<string> { $"FREQ={recurrence.Frequency.ToUpperInvariant()}" };

        if (recurrence.Interval > 1)
        {
            parts.Add($"INTERVAL={recurrence.Interval}");
        }

        if (recurrence.Frequency.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase) && recurrence.ByDay.Count > 0)
        {
            parts.Add($"BYDAY={string.Join(',', recurrence.ByDay.Select(d => d.ToUpperInvariant()))}");
        }

        if (recurrence.Count is { } n)
        {
            parts.Add($"COUNT={n}");
        }
        else if (recurrence.UntilUtc is { } until)
        {
            parts.Add($"UNTIL={DateTime.SpecifyKind(until, DateTimeKind.Utc):yyyyMMdd'T'HHmmss'Z'}");
        }

        return string.Join(';', parts);
    }

    private static bool TryParseUntil(string value, out DateTime until)
    {
        // RFC 5545 UNTIL is a UTC date-time (…Z) or a date; accept both.
        var formats = new[] { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd" };
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            until = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        until = default;
        return false;
    }
}
