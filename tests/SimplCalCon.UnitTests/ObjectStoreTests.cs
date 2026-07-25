using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Objects.Exceptions;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Storage;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

public sealed class ObjectStoreTests
{
    private const string Event = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:event-1@test
        SUMMARY:Team meeting
        DTSTART:20260715T090000Z
        DTEND:20260715T100000Z
        END:VEVENT
        END:VCALENDAR
        """;

    private const string EventWithLocation = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VEVENT
        UID:event-loc@test
        SUMMARY:Standup
        LOCATION:Room 4B
        DTSTART:20260715T090000Z
        DTEND:20260715T093000Z
        END:VEVENT
        END:VCALENDAR
        """;

    private const string Task = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//EN
        BEGIN:VTODO
        UID:task-1@test
        SUMMARY:Buy milk
        END:VTODO
        END:VCALENDAR
        """;

    private const string Contact = """
        BEGIN:VCARD
        VERSION:3.0
        UID:contact-1@test
        FN:Jane Doe
        N:Doe;Jane;;;
        ORG:Acme
        EMAIL:jane@example.com
        TEL:+1234567890
        END:VCARD
        """;

    private readonly TestDatabase _database = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private ObjectStore Store() => StoreFactory.ObjectStore(_database.CreateContext(), _clock);

    [Fact]
    public async Task Put_event_extracts_fields_and_starts_history()
    {
        var calendarId = await SeedCalendarAsync();

        var result = await Store().PutAsync(new PutObjectRequest(calendarId, "event-1.ics", Event, null), default);

        Assert.True(result.Created);
        Assert.Equal("event-1@test", result.Uid);
        Assert.Equal(1, result.RevisionNumber);

        await using var context = _database.CreateContext();
        var stored = await context.CalendarObjects.FirstAsync(o => o.CollectionId == calendarId);
        Assert.Equal("Team meeting", stored.Summary);
        Assert.Equal(new DateTime(2026, 7, 15, 9, 0, 0, DateTimeKind.Utc), stored.DtStartUtc);
        Assert.Equal(CalendarComponentType.Event, stored.ComponentType);
        Assert.Equal(1, await context.ObjectRevisions.CountAsync(r => r.ObjectId == stored.Id));
        Assert.Equal(1, (await context.Collections.FirstAsync(c => c.Id == calendarId)).ChangeSequence);
    }

    [Fact]
    public async Task Put_event_extracts_location()
    {
        var calendarId = await SeedCalendarAsync();

        await Store().PutAsync(new PutObjectRequest(calendarId, "event-loc.ics", EventWithLocation, null), default);

        await using var context = _database.CreateContext();
        var stored = await context.CalendarObjects.FirstAsync(o => o.CollectionId == calendarId);
        Assert.Equal("Room 4B", stored.Location);
    }

    [Fact]
    public async Task Update_then_delete_tracks_revisions_and_sequence()
    {
        var calendarId = await SeedCalendarAsync();
        await Store().PutAsync(new PutObjectRequest(calendarId, "event-1.ics", Event, null), default);
        await Store().PutAsync(new PutObjectRequest(calendarId, "event-1.ics", Event.Replace("Team meeting", "Renamed"), null), default);
        var deleted = await Store().DeleteAsync(calendarId, "event-1.ics", null, default);

        Assert.True(deleted);
        await using var context = _database.CreateContext();
        var stored = await context.CalendarObjects.FirstAsync(o => o.CollectionId == calendarId);
        Assert.True(stored.IsDeleted);
        Assert.Equal(3, stored.RevisionNumber);
        Assert.Equal(3, await context.ObjectRevisions.CountAsync(r => r.ObjectId == stored.Id));
        Assert.Equal(3, (await context.Collections.FirstAsync(c => c.Id == calendarId)).ChangeSequence);
    }

    [Fact]
    public async Task Duplicate_uid_in_another_resource_is_rejected()
    {
        var calendarId = await SeedCalendarAsync();
        await Store().PutAsync(new PutObjectRequest(calendarId, "event-1.ics", Event, null), default);

        await Assert.ThrowsAsync<UidConflictException>(() =>
            Store().PutAsync(new PutObjectRequest(calendarId, "different.ics", Event, null), default));
    }

    [Fact]
    public async Task Task_into_events_only_calendar_is_rejected()
    {
        var calendarId = await SeedCalendarAsync(supportsTasks: false);

        await Assert.ThrowsAsync<ComponentNotAllowedException>(() =>
            Store().PutAsync(new PutObjectRequest(calendarId, "task-1.ics", Task, null), default));
    }

    [Fact]
    public async Task Put_contact_extracts_name_and_email()
    {
        var addressBookId = await SeedAddressBookAsync();

        await Store().PutAsync(new PutObjectRequest(addressBookId, "contact-1.vcf", Contact, null), default);

        await using var context = _database.CreateContext();
        var stored = await context.ContactObjects.FirstAsync(o => o.CollectionId == addressBookId);
        Assert.Equal("Jane Doe", stored.FormattedName);
        Assert.Equal("Doe", stored.FamilyName);
        Assert.Equal("Jane", stored.GivenName);
        Assert.Equal("Acme", stored.Organization);
        Assert.Contains("jane@example.com", stored.Emails);
    }

    [Fact]
    public async Task Import_then_export_roundtrips_events()
    {
        var calendarId = await SeedCalendarAsync();
        var twoEvents = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Test//EN
            BEGIN:VEVENT
            UID:a@test
            SUMMARY:A
            DTSTART:20260101T090000Z
            END:VEVENT
            BEGIN:VEVENT
            UID:b@test
            SUMMARY:B
            DTSTART:20260102T090000Z
            END:VEVENT
            END:VCALENDAR
            """;

        var importCtx = _database.CreateContext();
        var importExport = new ObjectImportExport(importCtx, Store(), new DavRepository(importCtx, _clock));
        var outcome = await importExport.ImportAsync(calendarId, twoEvents, ImportConflictMode.Replace, null, default);

        Assert.Equal(2, outcome.Imported);
        Assert.Equal(0, outcome.Failed);

        var exported = await importExport.ExportAsync(calendarId, default);
        Assert.Contains("a@test", exported);
        Assert.Contains("b@test", exported);
    }

    [Fact]
    public async Task Imports_a_multi_card_vcf_including_uid_less_google_style_cards()
    {
        // A Google-style export: multiple cards, some without a UID (vCard 3.0), grouped
        // properties. Regression for the (Uid, Blob) tuple swap that failed every contact import.
        var bookId = await SeedAddressBookAsync();
        await using var context = _database.CreateContext();
        var import = new ObjectImportExport(
            context, StoreFactory.ObjectStore(context, _clock), new DavRepository(context, _clock));

        var vcf =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:John Smith\r\nN:Smith;John;;;\r\n" +
            "EMAIL;TYPE=INTERNET;TYPE=HOME:john@gmail.com\r\nTEL;TYPE=CELL:+15551234567\r\nEND:VCARD\r\n" +
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Jane Doe\r\nitem1.EMAIL;TYPE=INTERNET:jane@example.com\r\n" +
            "item1.X-ABLabel:Work\r\nEND:VCARD\r\n";

        var outcome = await import.ImportAsync(bookId, vcf, ImportConflictMode.Replace, null, default);

        Assert.Equal(2, outcome.Imported);
        Assert.Equal(0, outcome.Failed);

        await using var check = _database.CreateContext();
        var names = await check.ContactObjects.Where(c => c.CollectionId == bookId && !c.IsDeleted)
            .Select(c => c.FormattedName).ToListAsync();
        Assert.Contains("John Smith", names);
        Assert.Contains("Jane Doe", names);
    }

    private Task<Guid> SeedCalendarAsync(bool supportsTasks = true) =>
        SeedCollectionAsync(owner => new Calendar
        {
            Id = Guid.NewGuid(),
            TenantId = owner.TenantId!.Value,
            OwnerId = owner.Id,
            Name = "Calendar",
            ResourceName = $"cal-{Guid.NewGuid():N}",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            SupportsEvents = true,
            SupportsTasks = supportsTasks,
        });

    private Task<Guid> SeedAddressBookAsync() =>
        SeedCollectionAsync(owner => new AddressBook
        {
            Id = Guid.NewGuid(),
            TenantId = owner.TenantId!.Value,
            OwnerId = owner.Id,
            Name = "Contacts",
            ResourceName = $"ab-{Guid.NewGuid():N}",
            CreatedAt = _clock.UtcNow.UtcDateTime,
        });

    private async Task<Guid> SeedCollectionAsync(Func<User, Collection> collectionFactory)
    {
        await using var context = _database.CreateContext();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Tenant",
            Slug = $"t-{Guid.NewGuid():N}",
            CreatedAt = _clock.UtcNow,
        };
        var owner = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            DisplayName = "Owner",
            Email = $"owner-{Guid.NewGuid():N}@test.local",
            NormalizedEmail = $"OWNER-{Guid.NewGuid():N}@TEST.LOCAL",
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            CreatedAt = _clock.UtcNow,
        };
        var collection = collectionFactory(owner);

        context.AddRange(tenant, owner, collection);
        await context.SaveChangesAsync();
        return collection.Id;
    }
}
