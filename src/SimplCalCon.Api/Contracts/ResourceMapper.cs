using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Api.Contracts;

/// <summary>Maps domain entities to REST resources with their hypermedia links (ADR 0009).</summary>
internal static class ResourceMapper
{
    public static CalendarResource MapCalendar(Calendar calendar, Guid currentUserId) => new()
    {
        Id = calendar.Id,
        ResourceName = calendar.ResourceName,
        Name = calendar.Name,
        Color = calendar.Color,
        SupportsEvents = calendar.SupportsEvents,
        SupportsTasks = calendar.SupportsTasks,
        Shared = calendar.OwnerId != currentUserId,
        ConcurrencyToken = calendar.ConcurrencyToken,
        Links =
        {
            new Link("self", $"/api/calendars/{calendar.Id}"),
            new Link("events", $"/api/calendars/{calendar.Id}/events"),
        },
    };

    public static AddressBookResource MapAddressBook(AddressBook addressBook, Guid currentUserId) => new()
    {
        Id = addressBook.Id,
        ResourceName = addressBook.ResourceName,
        Name = addressBook.Name,
        Shared = addressBook.OwnerId != currentUserId,
        ConcurrencyToken = addressBook.ConcurrencyToken,
        Links =
        {
            new Link("self", $"/api/address-books/{addressBook.Id}"),
            new Link("contacts", $"/api/address-books/{addressBook.Id}/contacts"),
        },
    };

    public static EventResource MapEvent(CalendarObject calendarObject) => new()
    {
        Id = calendarObject.Id,
        ResourceName = calendarObject.ResourceName,
        Summary = calendarObject.Summary,
        StartUtc = calendarObject.DtStartUtc,
        EndUtc = calendarObject.DtEndUtc,
        IsAllDay = calendarObject.IsAllDay,
        IsRecurring = calendarObject.IsRecurring,
        DeletedAt = calendarObject.IsDeleted ? calendarObject.DeletedAt : null,
        ConcurrencyToken = calendarObject.ConcurrencyToken,
        Links = { new Link("self", $"/api/calendars/{calendarObject.CollectionId}/events/{calendarObject.Id}") },
    };

    public static RevisionResource MapRevision(ObjectRevision revision, string selfBase) => new()
    {
        RevisionNumber = revision.RevisionNumber,
        Operation = revision.Operation.ToString(),
        CreatedAt = revision.CreatedAt,
        AuthorPrincipalId = revision.AuthorPrincipalId,
        Links = { new Link("restore", $"{selfBase}/revisions/{revision.RevisionNumber}/restore") },
    };

    public static ContactResource MapContact(ContactObject contact) => new()
    {
        Id = contact.Id,
        ResourceName = contact.ResourceName,
        FormattedName = contact.FormattedName,
        FamilyName = contact.FamilyName,
        GivenName = contact.GivenName,
        Organization = contact.Organization,
        Emails = Split(contact.Emails),
        Phones = Split(contact.Phones),
        DeletedAt = contact.IsDeleted ? contact.DeletedAt : null,
        ConcurrencyToken = contact.ConcurrencyToken,
        Links = { new Link("self", $"/api/address-books/{contact.CollectionId}/contacts/{contact.Id}") },
    };

    private static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrEmpty(joined) ? [] : joined.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
