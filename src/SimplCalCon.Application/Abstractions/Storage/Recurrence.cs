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
    DateTime? UntilUtc,
    int? ByMonthDay = null);

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
        int? byMonthDay = null;

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
                    // Collected raw; validated against the frequency below (plain for weekly, one ordinal for monthly).
                    byDay.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;

                case "BYMONTHDAY":
                    if (!int.TryParse(value, out var day) || day is < 1 or > 31)
                    {
                        return false;
                    }

                    byMonthDay = day;
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
                    // Any other part (BYSETPOS, WKST, …) is outside the supported subset.
                    return false;
            }
        }

        // COUNT and UNTIL are mutually exclusive.
        if (frequency is null || (count is not null && until is not null))
        {
            return false;
        }

        switch (frequency)
        {
            case "WEEKLY":
                // Plain weekday codes only — an ordinal prefix (2MO) is beyond the editor; BYMONTHDAY is meaningless.
                if (byMonthDay is not null || byDay.Any(d => !Weekdays.Contains(d)))
                {
                    return false;
                }

                break;

            case "MONTHLY":
                // Either "on day N" (BYMONTHDAY) or a single ordinal weekday (e.g. 2TU, -1FR) — not both.
                if (byMonthDay is not null && byDay.Count > 0)
                {
                    return false;
                }

                if (byDay.Count > 1 || (byDay.Count == 1 && !IsOrdinalWeekday(byDay[0])))
                {
                    return false;
                }

                break;

            default:
                // Daily / yearly carry no BYDAY / BYMONTHDAY here.
                if (byDay.Count > 0 || byMonthDay is not null)
                {
                    return false;
                }

                break;
        }

        recurrence = new Recurrence(frequency, interval, byDay, count, until, byMonthDay);
        return true;
    }

    // A monthly ordinal weekday token: an ordinal in {1,2,3,4,-1} followed by a weekday, e.g. "2TU", "-1FR".
    private static bool IsOrdinalWeekday(string token)
    {
        if (token.Length < 3)
        {
            return false;
        }

        var weekday = token[^2..];
        var ordinalText = token[..^2];
        return Weekdays.Contains(weekday)
            && int.TryParse(ordinalText, out var ordinal)
            && ordinal is 1 or 2 or 3 or 4 or -1;
    }

    public static string Format(Recurrence recurrence)
    {
        var parts = new List<string> { $"FREQ={recurrence.Frequency.ToUpperInvariant()}" };

        if (recurrence.Interval > 1)
        {
            parts.Add($"INTERVAL={recurrence.Interval}");
        }

        var isWeekly = recurrence.Frequency.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase);
        var isMonthly = recurrence.Frequency.Equals("MONTHLY", StringComparison.OrdinalIgnoreCase);

        if (isWeekly && recurrence.ByDay.Count > 0)
        {
            parts.Add($"BYDAY={string.Join(',', recurrence.ByDay.Select(d => d.ToUpperInvariant()))}");
        }
        else if (isMonthly && recurrence.ByMonthDay is { } monthDay)
        {
            parts.Add($"BYMONTHDAY={monthDay}");
        }
        else if (isMonthly && recurrence.ByDay.Count == 1)
        {
            parts.Add($"BYDAY={recurrence.ByDay[0].ToUpperInvariant()}");
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
