using Ical.Net.DataTypes;
using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Free/busy computation over a user's owned calendars (ADR 0030), recurrence expanded via Ical.Net.</summary>
internal sealed class FreeBusyService(SimplCalConDbContext dbContext) : IFreeBusyService
{
    public async Task<IReadOnlyList<BusyPeriod>> GetBusyAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var calendarIds = await dbContext.Calendars
            .Where(c => c.OwnerId == userId && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        // SQL-prefilter (mirrors the calendar-query path): non-recurring events overlapping the
        // window, plus all recurring candidates (expanded precisely below).
        var candidates = await dbContext.CalendarObjects
            .Where(o => calendarIds.Contains(o.CollectionId) && !o.IsDeleted
                && o.ComponentType == CalendarComponentType.Event
                && (o.IsRecurring || (o.DtStartUtc < toUtc && (o.DtEndUtc ?? o.DtStartUtc) > fromUtc)))
            .Select(o => new { o.Blob, o.IsRecurring, o.DtStartUtc, o.DtEndUtc })
            .ToListAsync(cancellationToken);

        var periods = new List<BusyPeriod>();
        foreach (var candidate in candidates)
        {
            if (!candidate.IsRecurring)
            {
                // TRANSP:TRANSPARENT events do not block time (RFC 5545) — exclude from free/busy.
                if (candidate.DtStartUtc is { } start && !IsTransparent(candidate.Blob))
                {
                    Add(periods, start, candidate.DtEndUtc ?? start, fromUtc, toUtc);
                }
            }
            else
            {
                Expand(candidate.Blob, fromUtc, toUtc, periods);
            }
        }

        return Merge(periods);
    }

    public async Task<Guid?> ResolveUserAsync(Guid tenantId, string calendarUserAddress, CancellationToken cancellationToken)
    {
        var email = StripScheme(calendarUserAddress).ToUpperInvariant();
        return await dbContext.Users
            .Where(u => u.TenantId == tenantId && u.NormalizedEmail == email && u.Status == UserStatus.Active)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string StripScheme(string address)
    {
        var trimmed = address.Trim();
        var colon = trimmed.IndexOf(':');
        return colon >= 0 && trimmed[..colon].Equals("mailto", StringComparison.OrdinalIgnoreCase)
            ? trimmed[(colon + 1)..]
            : trimmed;
    }

    private static void Expand(string blob, DateTime fromUtc, DateTime toUtc, List<BusyPeriod> periods)
    {
        Ical.Net.Calendar? calendar;
        try
        {
            calendar = Ical.Net.Calendar.Load(blob);
        }
        catch (Exception)
        {
            return;
        }

        if (calendar is null)
        {
            return;
        }

        // Look back by the event duration so an occurrence that started before the window but runs into
        // it still counts as busy (true overlap — RFC 4791); Add() clips to [fromUtc, toUtc).
        var from = new CalDateTime(DateTime.SpecifyKind(fromUtc - MaxDuration(calendar), DateTimeKind.Utc));
        foreach (var occurrence in calendar.GetOccurrences(from))
        {
            if (occurrence.Period.StartTime?.AsUtc is not { } start)
            {
                continue;
            }

            if (start >= toUtc)
            {
                break;
            }

            // Per-occurrence TRANSP (an override can differ from the master): transparent occurrences
            // do not block time (RFC 5545), so they're excluded from free/busy.
            if (occurrence.Source is Ical.Net.CalendarComponents.CalendarEvent source
                && string.Equals(source.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = occurrence.Period.EffectiveEndTime?.AsUtc ?? start; // EndTime is null on occurrences; use the computed end
            Add(periods, DateTime.SpecifyKind(start, DateTimeKind.Utc), DateTime.SpecifyKind(end, DateTimeKind.Utc), fromUtc, toUtc);
        }
    }

    // A single VEVENT's TRANSP read at the line level (TRANSP takes no parameters per RFC 5545), so the
    // non-recurring path needn't full-parse the blob it already has in hand.
    private static bool IsTransparent(string blob) =>
        blob.Split('\n').Any(line => line.Trim().Equals("TRANSP:TRANSPARENT", StringComparison.OrdinalIgnoreCase));

    // The longest master-event duration in the blob — the look-back needed so a spanning occurrence isn't skipped.
    private static TimeSpan MaxDuration(Ical.Net.Calendar calendar)
    {
        var max = TimeSpan.Zero;
        foreach (var ev in calendar.Events)
        {
            if (ev.DtStart?.AsUtc is { } s && ev.DtEnd?.AsUtc is { } e && e > s && e - s > max)
            {
                max = e - s;
            }
        }

        return max;
    }

    private static void Add(List<BusyPeriod> periods, DateTime start, DateTime end, DateTime fromUtc, DateTime toUtc)
    {
        var clippedStart = start < fromUtc ? fromUtc : start;
        var clippedEnd = end > toUtc ? toUtc : end;
        if (clippedEnd > clippedStart)
        {
            periods.Add(new BusyPeriod(clippedStart, clippedEnd));
        }
    }

    // Sort by start and coalesce overlapping/adjacent windows.
    private static List<BusyPeriod> Merge(List<BusyPeriod> periods)
    {
        if (periods.Count == 0)
        {
            return periods;
        }

        var ordered = periods.OrderBy(p => p.StartUtc).ToList();
        var merged = new List<BusyPeriod> { ordered[0] };
        foreach (var period in ordered.Skip(1))
        {
            var last = merged[^1];
            if (period.StartUtc <= last.EndUtc)
            {
                merged[^1] = last with { EndUtc = period.EndUtc > last.EndUtc ? period.EndUtc : last.EndUtc };
            }
            else
            {
                merged.Add(period);
            }
        }

        return merged;
    }
}
