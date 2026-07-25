using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Storage;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

/// <summary>
/// The occurrence-window index (ADR 0061): materialization on write, and — the correctness contract —
/// that time-range queries return the same objects whether served from the index (covered ranges) or
/// the on-the-fly expansion fallback (ranges beyond the materialized window).
/// </summary>
public sealed class OccurrenceIndexTests
{
    // A small window keeps row counts (and the test) tiny while still exercising truncation.
    private static readonly OccurrenceOptions SmallWindow = new() { PastDays = 30, FutureDays = 60 };

    // Clock at 2026-06-01 → materialized window [2026-05-02, 2026-07-31).
    private readonly TestDatabase _database = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

    private static string WeeklyBounded(string uid) =>
        $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:{uid}\r\n" +
        "DTSTART:20260602T090000Z\r\nDTEND:20260602T093000Z\r\nSUMMARY:Weekly\r\n" +
        "RRULE:FREQ=WEEKLY;COUNT=4\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static string DailyUnbounded(string uid) =>
        $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:{uid}\r\n" +
        "DTSTART:20260602T090000Z\r\nDTEND:20260602T093000Z\r\nSUMMARY:Daily\r\n" +
        "RRULE:FREQ=DAILY\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static string Single(string uid) =>
        $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:{uid}\r\n" +
        "DTSTART:20260610T090000Z\r\nDTEND:20260610T100000Z\r\nSUMMARY:Single\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    [Fact]
    public async Task Bounded_recurrence_within_window_is_complete_and_materialized()
    {
        var calendarId = await SeedCalendarAsync();
        await PutAsync(calendarId, "weekly.ics", WeeklyBounded("weekly@t"));

        await using var context = _database.CreateContext();
        var stored = await context.CalendarObjects.FirstAsync(o => o.Uid == "weekly@t");
        Assert.True(stored.OccurrencesComplete);
        Assert.Null(stored.OccurrencesUntilUtc);
        Assert.Equal(4, await context.EventOccurrences.CountAsync(o => o.ObjectId == stored.Id));
    }

    [Fact]
    public async Task Unbounded_recurrence_is_incomplete_and_bounded_to_the_window()
    {
        var calendarId = await SeedCalendarAsync();
        await PutAsync(calendarId, "daily.ics", DailyUnbounded("daily@t"));

        await using var context = _database.CreateContext();
        var stored = await context.CalendarObjects.FirstAsync(o => o.Uid == "daily@t");
        Assert.False(stored.OccurrencesComplete);
        // Until is the window end (2026-07-31); rows exist up to there, none beyond.
        Assert.Equal(new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc), stored.OccurrencesUntilUtc);
        var rows = await context.EventOccurrences.Where(o => o.ObjectId == stored.Id).ToListAsync();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.StartUtc < stored.OccurrencesUntilUtc));
    }

    [Fact]
    public async Task Non_recurring_event_gets_no_rows_and_is_marked_complete()
    {
        var calendarId = await SeedCalendarAsync();
        await PutAsync(calendarId, "single.ics", Single("single@t"));

        await using var context = _database.CreateContext();
        var stored = await context.CalendarObjects.FirstAsync(o => o.Uid == "single@t");
        Assert.True(stored.OccurrencesComplete);
        Assert.Equal(0, await context.EventOccurrences.CountAsync(o => o.ObjectId == stored.Id));
    }

    [Fact]
    public async Task Delete_removes_index_rows()
    {
        var calendarId = await SeedCalendarAsync();
        await PutAsync(calendarId, "daily.ics", DailyUnbounded("daily@t"));
        await StoreFactory.ObjectStore(_database.CreateContext(), _clock, SmallWindow)
            .DeleteAsync(calendarId, "daily.ics", null, default);

        await using var context = _database.CreateContext();
        Assert.Equal(0, await context.EventOccurrences.CountAsync());
    }

    [Fact]
    public async Task In_window_query_uses_the_index_and_returns_covered_events()
    {
        var calendarId = await SeedCalendarAsync();
        await PutAsync(calendarId, "weekly.ics", WeeklyBounded("weekly@t"));   // Complete → index only
        await PutAsync(calendarId, "daily.ics", DailyUnbounded("daily@t"));    // covered in-window → index

        // A range fully inside the window is served entirely from the index (no fallback expansion).
        var hits = await QueryAsync(calendarId, new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));

        Assert.Equal(["daily@t", "weekly@t"], hits);
    }

    [Fact]
    public async Task Beyond_window_query_falls_back_to_expansion()
    {
        var calendarId = await SeedCalendarAsync();
        await PutAsync(calendarId, "weekly.ics", WeeklyBounded("weekly@t"));
        await PutAsync(calendarId, "daily.ics", DailyUnbounded("daily@t"));

        // September is past the materialized window: the unbounded daily still occurs there (found via
        // fallback expansion), the 4-week series does not. Proves the fallback path stays correct.
        var hits = await QueryAsync(calendarId, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30));

        Assert.Equal(["daily@t"], hits);
    }

    [Fact]
    public async Task Roll_forward_refreshes_the_window_without_bumping_the_etag()
    {
        var calendarId = await SeedCalendarAsync();
        await PutAsync(calendarId, "daily.ics", DailyUnbounded("daily@t"));

        Guid tokenBefore;
        await using (var read = _database.CreateContext())
        {
            tokenBefore = (await read.CalendarObjects.FirstAsync(o => o.Uid == "daily@t")).ConcurrencyToken;
        }

        // Advance well past the window and re-materialize via the roll-forward path.
        _clock.Advance(TimeSpan.FromDays(40));
        var newNow = _clock.UtcNow.UtcDateTime;
        await using (var context = _database.CreateContext())
        {
            var stored = await context.CalendarObjects.FirstAsync(o => o.Uid == "daily@t");
            await StoreFactory.Indexer(context, SmallWindow).RollForwardAsync(stored, newNow, default);
            await context.SaveChangesAsync();
        }

        await using var check = _database.CreateContext();
        var refreshed = await check.CalendarObjects.FirstAsync(o => o.Uid == "daily@t");
        // The ETag/concurrency token must be untouched — an internal refresh is not an edit (ADR 0061).
        Assert.Equal(tokenBefore, refreshed.ConcurrencyToken);
        // The future horizon has rolled forward to the new "now".
        Assert.Equal(newNow.AddDays(SmallWindow.FutureDays), refreshed.OccurrencesUntilUtc);
    }

    [Fact]
    public async Task Query_finds_a_recurring_occurrence_spanning_into_the_window()
    {
        var calendarId = await SeedCalendarAsync();
        // Weekly 3-day event, first occurrence Jun 8 00:00 → Jun 11 00:00 (within the materialized window).
        await PutAsync(calendarId, "span.ics",
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:span@t\r\n" +
            "DTSTART:20260608T000000Z\r\nDTEND:20260611T000000Z\r\nSUMMARY:Span\r\nRRULE:FREQ=WEEKLY;COUNT=4\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n");

        // Jun 9 12:00 → Jun 10 12:00 is mid-occurrence (the occurrence started Jun 8, before this window).
        var hits = await QueryAsync(calendarId, new DateTime(2026, 6, 9, 12, 0, 0), new DateTime(2026, 6, 10, 12, 0, 0));

        Assert.Equal(["span@t"], hits);
    }

    [Fact]
    public async Task Index_and_fallback_agree_with_pure_expansion_across_ranges()
    {
        var calendarId = await SeedCalendarAsync();
        await PutAsync(calendarId, "weekly.ics", WeeklyBounded("weekly@t"));
        await PutAsync(calendarId, "daily.ics", DailyUnbounded("daily@t"));
        await PutAsync(calendarId, "single.ics", Single("single@t"));

        (DateTime Start, DateTime End)[] ranges =
        [
            (new(2026, 6, 1), new(2026, 6, 30)),   // in-window
            (new(2026, 6, 10), new(2026, 6, 11)),  // single-day, in-window
            (new(2026, 9, 1), new(2026, 9, 30)),   // beyond window (fallback)
            (new(2026, 1, 1), new(2026, 2, 1)),    // before the events start
            (new(2030, 1, 1), new(2030, 2, 1)),    // far future
        ];

        foreach (var (start, end) in ranges)
        {
            var actual = await QueryAsync(calendarId, start, end);
            var expected = await PureExpansionAsync(calendarId, start, end);
            Assert.Equal(expected, actual);
        }
    }

    // The pre-index reference: non-recurring by column overlap, recurring by on-the-fly expansion.
    private async Task<List<string>> PureExpansionAsync(Guid calendarId, DateTime start, DateTime end)
    {
        await using var context = _database.CreateContext();
        var all = await context.CalendarObjects.Where(o => o.CollectionId == calendarId && !o.IsDeleted).ToListAsync();
        return all
            .Where(o => o.IsRecurring
                ? CalendarOccurrence.OverlapsRange(o.Blob, start, end)
                : o.DtStartUtc == null || (o.DtStartUtc < end && (o.DtEndUtc ?? o.DtStartUtc) >= start))
            .Select(o => o.Uid).OrderBy(u => u).ToList();
    }

    private async Task<List<string>> QueryAsync(Guid calendarId, DateTime start, DateTime end)
    {
        await using var context = _database.CreateContext();
        var repository = new DavRepository(context, _clock);
        var hits = await repository.QueryCalendarObjectsAsync(calendarId, start, end, default);
        return hits.Select(o => o.Uid).OrderBy(u => u).ToList();
    }

    private Task PutAsync(Guid calendarId, string resourceName, string blob) =>
        StoreFactory.ObjectStore(_database.CreateContext(), _clock, SmallWindow)
            .PutAsync(new PutObjectRequest(calendarId, resourceName, blob, null), default);

    private async Task<Guid> SeedCalendarAsync()
    {
        await using var context = _database.CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "T", Slug = $"t-{Guid.NewGuid():N}", CreatedAt = _clock.UtcNow };
        var owner = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            DisplayName = "Owner",
            Email = $"o-{Guid.NewGuid():N}@t.local",
            NormalizedEmail = $"O-{Guid.NewGuid():N}@T.LOCAL",
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            CreatedAt = _clock.UtcNow,
        };
        var calendar = new Calendar
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            OwnerId = owner.Id,
            Name = "Calendar",
            ResourceName = $"cal-{Guid.NewGuid():N}",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            SupportsEvents = true,
            SupportsTasks = true,
        };
        context.AddRange(tenant, owner, calendar);
        await context.SaveChangesAsync();
        return calendar.Id;
    }
}
