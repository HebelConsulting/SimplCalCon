using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Storage;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

/// <summary>Free/busy excludes TRANSP:TRANSPARENT events (RFC 5545) — the ADR 0030 simplification, fixed.</summary>
public sealed class FreeBusyServiceTests
{
    private readonly TestDatabase _database = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

    private static readonly DateTime From = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);

    private static string Event(string uid, string startEnd, bool transparent) =>
        $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//T//EN\r\nBEGIN:VEVENT\r\nUID:{uid}\r\nSUMMARY:{uid}\r\n" +
        startEnd + (transparent ? "TRANSP:TRANSPARENT\r\n" : "") + "END:VEVENT\r\nEND:VCALENDAR\r\n";

    private const string OpaqueTimes = "DTSTART:20260715T100000Z\r\nDTEND:20260715T110000Z\r\n";
    private const string TransparentTimes = "DTSTART:20260715T120000Z\r\nDTEND:20260715T130000Z\r\n";

    [Fact]
    public async Task Transparent_non_recurring_event_does_not_block_time()
    {
        var (calendarId, ownerId) = await SeedAsync();
        await PutAsync(calendarId, "opaque.ics", Event("opaque", OpaqueTimes, transparent: false));
        await PutAsync(calendarId, "free.ics", Event("free", TransparentTimes, transparent: true));

        var busy = await GetBusyAsync(ownerId);

        var period = Assert.Single(busy);
        Assert.Equal(new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 7, 15, 11, 0, 0, DateTimeKind.Utc), period.EndUtc);
    }

    [Fact]
    public async Task Transparent_recurring_series_is_excluded()
    {
        var (calendarId, ownerId) = await SeedAsync();
        await PutAsync(calendarId, "opaque.ics", Event("opaque", OpaqueTimes, transparent: false));
        await PutAsync(calendarId, "daily-free.ics",
            Event("dailyfree", TransparentTimes + "RRULE:FREQ=DAILY;COUNT=3\r\n", transparent: true));

        var busy = await GetBusyAsync(ownerId);

        Assert.Single(busy); // only the opaque event; the transparent daily series blocks nothing
    }

    [Fact]
    public async Task Opaque_events_still_count_as_busy()
    {
        var (calendarId, ownerId) = await SeedAsync();
        await PutAsync(calendarId, "a.ics", Event("a", OpaqueTimes, transparent: false));
        await PutAsync(calendarId, "b.ics", Event("b", TransparentTimes, transparent: false)); // opaque, different window

        var busy = await GetBusyAsync(ownerId);

        Assert.Equal(2, busy.Count);
    }

    private async Task<IReadOnlyList<BusyPeriod>> GetBusyAsync(Guid ownerId)
    {
        await using var context = _database.CreateContext();
        return await new FreeBusyService(context).GetBusyAsync(ownerId, From, To, default);
    }

    private Task PutAsync(Guid calendarId, string resourceName, string blob) =>
        StoreFactory.ObjectStore(_database.CreateContext(), _clock)
            .PutAsync(new PutObjectRequest(calendarId, resourceName, blob, null), default);

    private async Task<(Guid CalendarId, Guid OwnerId)> SeedAsync()
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
        return (calendar.Id, owner.Id);
    }
}
