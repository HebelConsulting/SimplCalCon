using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Bound from <c>SimplCalCon:Occurrences</c> — the occurrence-window index (ADR 0061).</summary>
public sealed class OccurrenceOptions
{
    /// <summary>How far back to materialize occurrences from "now" (a query before this falls back).</summary>
    public int PastDays { get; set; } = 365;

    /// <summary>How far forward to materialize occurrences from "now" (a query beyond this falls back).</summary>
    public int FutureDays { get; set; } = 730;

    /// <summary>Safety cap on rows per object; a pathological rule stops here and queries beyond fall back.</summary>
    public int MaxRowsPerObject { get; set; } = 2000;

    /// <summary>Whether the roll-forward background sweep runs (keeps the future horizon fresh as time passes).</summary>
    public bool RollForwardEnabled { get; set; } = true;

    /// <summary>Roll-forward cadence in hours (floored at 1).</summary>
    public int RollForwardHours { get; set; } = 24;

    /// <summary>Objects re-materialized per roll-forward batch.</summary>
    public int RollForwardBatch { get; set; } = 200;

    /// <summary>Re-materialize an incomplete object once its future coverage drops within this many days of "now".</summary>
    public int RefreshBelowDays { get; set; } = 365;
}

/// <summary>
/// Maintains the occurrence-window index (ADR 0061): expands a recurring event into
/// <see cref="EventOccurrence"/> rows within a rolling <c>[now-PastDays, now+FutureDays]</c> window and
/// records how far it's covered on the <see cref="CalendarObject"/>. Non-recurring events are served
/// from their <c>DtStartUtc</c>/<c>DtEndUtc</c> columns, so they get no rows and are marked complete.
/// Both entry points run inside the caller's transaction, so the delete-and-rebuild is atomic — a
/// partial failure never leaves "covered" flags pointing at missing rows.
/// </summary>
internal sealed class OccurrenceIndexer(SimplCalConDbContext dbContext, IOptions<OccurrenceOptions> options)
{
    /// <summary>
    /// Write path: rebuild the rows and set the window flags on the tracked entity, so the caller's
    /// <c>SaveChanges</c> commits the object, its occurrence rows, and one ETag regeneration together
    /// (this is a genuine content change, so bumping the ETag is correct). Call this before the save.
    /// </summary>
    public async Task RebuildAsync(CalendarObject calendarObject, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var (complete, from, until) = await ReplaceRowsAsync(calendarObject, nowUtc, cancellationToken);
        calendarObject.OccurrencesComplete = complete;
        calendarObject.OccurrencesFromUtc = from;
        calendarObject.OccurrencesUntilUtc = until;
    }

    /// <summary>
    /// Roll-forward path: rebuild the rows and update the window-flag columns via <c>ExecuteUpdate</c>,
    /// which does NOT mark the entity modified — so the object's ETag/concurrency token is left alone.
    /// An internal index refresh must not look like an edit (it would cause spurious <c>If-Match</c>
    /// 412s) nor bump the collection change sequence. The caller's <c>SaveChanges</c> flushes the rows.
    /// </summary>
    public async Task RollForwardAsync(CalendarObject calendarObject, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var (complete, from, until) = await ReplaceRowsAsync(calendarObject, nowUtc, cancellationToken);
        await dbContext.CalendarObjects
            .Where(o => o.Id == calendarObject.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(o => o.OccurrencesComplete, complete)
                .SetProperty(o => o.OccurrencesFromUtc, from)
                .SetProperty(o => o.OccurrencesUntilUtc, until), cancellationToken);
    }

    // Delete the object's rows and, for a recurring event, materialize fresh ones (Add — pending until
    // the caller saves). Returns the window-coverage flags. Deletes are immediate SQL (like
    // RebuildAttendeesAsync) rather than via the tracked nav, to avoid a phantom concurrency conflict.
    private async Task<(bool Complete, DateTime? From, DateTime? Until)> ReplaceRowsAsync(
        CalendarObject calendarObject, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await dbContext.EventOccurrences
            .Where(o => o.ObjectId == calendarObject.Id)
            .ExecuteDeleteAsync(cancellationToken);

        if (calendarObject.ComponentType != CalendarComponentType.Event || !calendarObject.IsRecurring)
        {
            return (true, null, null);
        }

        var opts = options.Value;
        var from = nowUtc.AddDays(-Math.Max(0, opts.PastDays));
        var to = nowUtc.AddDays(Math.Max(1, opts.FutureDays));
        var maxRows = Math.Max(1, opts.MaxRowsPerObject);
        var (windows, truncated) = CalendarOccurrence.Materialize(calendarObject.Blob, from, to, maxRows);

        foreach (var (startUtc, endUtc) in windows)
        {
            dbContext.EventOccurrences.Add(new EventOccurrence
            {
                Id = Guid.NewGuid(),
                ObjectId = calendarObject.Id,
                CollectionId = calendarObject.CollectionId,
                StartUtc = startUtc,
                EndUtc = endUtc,
            });
        }

        // Past side is complete when the series' first instance (DTSTART) is not before the window.
        var pastComplete = calendarObject.DtStartUtc is null || calendarObject.DtStartUtc >= from;
        var futureComplete = !truncated;
        if (pastComplete && futureComplete)
        {
            return (true, null, null);
        }

        // If the row cap (not the window end) stopped us, only the last materialized start is safe.
        var until = truncated && windows.Count >= maxRows && windows.Count > 0
            ? windows[^1].StartUtc
            : to;
        return (false, from, until);
    }
}
