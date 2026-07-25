using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// Read/query access the DAV surface needs over collections and objects, plus default
/// address-book provisioning (ADR 0003). Writes to objects go through
/// <see cref="IObjectStore"/>.
/// </summary>
public interface IDavRepository
{
    /// <summary>Returns the owner's default address book, creating one on first access.</summary>
    Task<AddressBook?> EnsureDefaultAddressBookAsync(Guid ownerId, Guid? tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AddressBook>> ListAddressBooksAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Address books the user owns or has a read grant on (directly or via a group) — ADR 0007.</summary>
    Task<IReadOnlyList<AddressBook>> ListAccessibleAddressBooksAsync(Guid userId, CancellationToken cancellationToken);

    Task<AddressBook?> GetAddressBookAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken);

    Task<AddressBook?> GetAddressBookByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<AddressBook> CreateAddressBookAsync(
        Guid ownerId, Guid tenantId, string resourceName, string? displayName, CancellationToken cancellationToken);

    Task<bool> DeleteAddressBookAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken);

    /// <summary>Updates a collection's display name and colour (ADR 0041/0062); returns the updated collection or null if absent.</summary>
    Task<Collection?> UpdateCollectionAsync(Guid collectionId, string newName, string? color, CancellationToken cancellationToken);

    /// <summary>Enables (generates a fresh token) or disables the read-only subscription feed (ADR 0069); returns the new token or null.</summary>
    Task<string?> SetFeedTokenAsync(Guid collectionId, bool enabled, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactObject>> ListObjectsAsync(Guid collectionId, CancellationToken cancellationToken);

    Task<ContactObject?> GetObjectAsync(Guid collectionId, string resourceName, CancellationToken cancellationToken);

    Task<ContactObject?> GetContactObjectByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactObject>> GetObjectsAsync(
        Guid collectionId, IReadOnlyCollection<string> resourceNames, CancellationToken cancellationToken);

    /// <summary>
    /// Objects changed and removed since <paramref name="sinceToken"/> (null = initial sync),
    /// plus the collection's current sync token (RFC 6578).
    /// </summary>
    Task<DavSyncResult> SyncAsync(Guid collectionId, long? sinceToken, CancellationToken cancellationToken);

    // --- CalDAV ---

    Task<Calendar?> EnsureDefaultCalendarAsync(Guid ownerId, Guid? tenantId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Calendar>> ListCalendarsAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>Calendars the user owns or has a read grant on (directly or via a group) — ADR 0007.</summary>
    Task<IReadOnlyList<Calendar>> ListAccessibleCalendarsAsync(Guid userId, CancellationToken cancellationToken);

    Task<Calendar?> GetCalendarAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken);

    Task<Calendar?> GetCalendarByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Calendar> CreateCalendarAsync(
        Guid ownerId,
        Guid tenantId,
        string resourceName,
        string? displayName,
        bool supportsEvents,
        bool supportsTasks,
        CancellationToken cancellationToken);

    Task<bool> DeleteCalendarAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarObject>> ListCalendarObjectsAsync(Guid collectionId, CancellationToken cancellationToken);

    Task<CalendarObject?> GetCalendarObjectAsync(Guid collectionId, string resourceName, CancellationToken cancellationToken);

    Task<CalendarObject?> GetCalendarObjectByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarObject>> GetCalendarObjectsAsync(
        Guid collectionId, IReadOnlyCollection<string> resourceNames, CancellationToken cancellationToken);

    /// <summary>
    /// Candidate objects for a time-range query: non-recurring objects overlapping the
    /// window plus all recurring objects (the caller expands those precisely). Null
    /// bounds mean an unbounded query (all live objects).
    /// </summary>
    Task<IReadOnlyList<CalendarObject>> QueryCalendarObjectsAsync(
        Guid collectionId, DateTime? startUtc, DateTime? endUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Expands calendar objects into their concrete occurrences starting within [startUtc, endUtc)
    /// for the web grid (ADR 0050): recurring masters are recurrence-expanded, non-recurring events
    /// pass through once. Reads the blob for recurring candidates (the on-demand-query exception, ADR 0004/0043).
    /// </summary>
    Task<IReadOnlyList<CalendarObjectOccurrence>> QueryCalendarOccurrencesAsync(
        Guid collectionId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken);

    /// <summary>Evaluates a CalDAV calendar-query filter (component + time-range + prop-filters, ADR 0043).</summary>
    Task<IReadOnlyList<CalendarObject>> QueryCalendarObjectsAsync(
        Guid collectionId, CalendarQueryFilter filter, CancellationToken cancellationToken);

    /// <summary>Evaluates a CardDAV addressbook-query filter (prop-filters over vCard properties, ADR 0043).</summary>
    Task<IReadOnlyList<ContactObject>> QueryContactObjectsAsync(
        Guid collectionId, ContactQueryFilter filter, CancellationToken cancellationToken);

    Task<DavCalendarSyncResult> SyncCalendarAsync(Guid collectionId, long? sinceToken, CancellationToken cancellationToken);

    // --- Trash & version history (ADR 0028) ---

    Task<IReadOnlyList<CalendarObject>> ListTrashedCalendarObjectsAsync(Guid collectionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactObject>> ListTrashedContactObjectsAsync(Guid collectionId, CancellationToken cancellationToken);

    /// <summary>Finds a calendar object by id including trashed ones (unlike <see cref="GetCalendarObjectByIdAsync"/>).</summary>
    Task<CalendarObject?> FindCalendarObjectByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Finds a contact object by id including trashed ones (unlike <see cref="GetContactObjectByIdAsync"/>).</summary>
    Task<ContactObject?> FindContactObjectByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>An object's revision history, newest first (ADR 0011).</summary>
    Task<IReadOnlyList<ObjectRevision>> ListObjectRevisionsAsync(Guid objectId, CancellationToken cancellationToken);
}

public sealed record DavSyncResult(
    IReadOnlyList<ContactObject> Changed, IReadOnlyList<string> RemovedResourceNames, long Token);

public sealed record DavCalendarSyncResult(
    IReadOnlyList<CalendarObject> Changed, IReadOnlyList<string> RemovedResourceNames, long Token);

/// <summary>
/// One expanded occurrence of a calendar object within a queried window (ADR 0050/0051).
/// <paramref name="Summary"/>/<paramref name="Location"/> are the occurrence's effective values
/// (an overridden instance carries its own), or null to fall back to the master's.
/// </summary>
public sealed record CalendarObjectOccurrence(
    CalendarObject Object, DateTime StartUtc, DateTime EndUtc, DateTime? RecurrenceIdUtc,
    string? Summary = null, string? Location = null);
