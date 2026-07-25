using System.Text.RegularExpressions;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Scheduling;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Api.Contracts;

/// <summary>Maps domain entities to REST resources with their hypermedia links (ADR 0009).</summary>
internal static partial class ResourceMapper
{
    public static InvitationResource MapInvitation(Invitation invitation) => new()
    {
        ResourceName = invitation.ResourceName,
        Uid = invitation.Uid,
        Summary = invitation.Summary,
        StartUtc = invitation.StartUtc,
        EndUtc = invitation.EndUtc,
        OrganizerEmail = invitation.OrganizerEmail,
        OrganizerName = invitation.OrganizerName,
        Links = { new Link("respond", "/api/invitations/respond") },
    };

    // A PHOTO property line: optional group prefix (item1.), then PHOTO, then ';' params or ':' value.
    [GeneratedRegex(@"^(?:[A-Za-z0-9-]+\.)?PHOTO[;:]", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PhotoLine();

    private static bool HasPhoto(string? blob) => !string.IsNullOrEmpty(blob) && PhotoLine().IsMatch(blob);

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

    public static EventResource MapEvent(CalendarObject calendarObject) =>
        MapEvent(calendarObject, calendarObject.DtStartUtc, calendarObject.DtEndUtc);

    /// <summary>Maps an event, overriding its times/fields with an expanded occurrence's window + RECURRENCE-ID (ADR 0050/0051).</summary>
    public static EventResource MapEvent(
        CalendarObject calendarObject, DateTime? startUtc, DateTime? endUtc, DateTime? recurrenceId = null,
        string? summaryOverride = null, string? locationOverride = null)
    {
        var supported = RecurrenceRule.TryParse(calendarObject.RecurrenceRule, out var recurrence);
        return new EventResource
        {
            RecurrenceId = recurrenceId,
            Id = calendarObject.Id,
            ResourceName = calendarObject.ResourceName,
            Summary = summaryOverride ?? calendarObject.Summary,
            Location = locationOverride ?? calendarObject.Location,
            StartUtc = startUtc,
            EndUtc = endUtc,
            IsAllDay = calendarObject.IsAllDay,
            IsRecurring = calendarObject.IsRecurring,
            Recurrence = supported ? recurrence : null,
            RecurrenceRule = calendarObject.RecurrenceRule,
            RecurrenceSupported = supported,
            Attendees = calendarObject.Attendees
                .OrderByDescending(a => a.IsOrganizer)
                .Select(a => new AttendeeResource
                {
                    Address = a.Address,
                    CommonName = a.CommonName,
                    Role = a.Role.ToString(),
                    ParticipationStatus = a.ParticipationStatus.ToString(),
                    IsOrganizer = a.IsOrganizer,
                })
                .ToList(),
            DeletedAt = calendarObject.IsDeleted ? calendarObject.DeletedAt : null,
            ConcurrencyToken = calendarObject.ConcurrencyToken,
            Links = { new Link("self", $"/api/calendars/{calendarObject.CollectionId}/events/{calendarObject.Id}") },
        };
    }

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
        HasPhoto = HasPhoto(contact.Blob),
        DeletedAt = contact.IsDeleted ? contact.DeletedAt : null,
        ConcurrencyToken = contact.ConcurrencyToken,
        Links = { new Link("self", $"/api/address-books/{contact.CollectionId}/contacts/{contact.Id}") },
    };

    private static IReadOnlyList<string> Split(string? joined) =>
        string.IsNullOrEmpty(joined) ? [] : joined.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
