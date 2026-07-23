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

    Task<AddressBook?> GetAddressBookAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken);

    Task<AddressBook> CreateAddressBookAsync(
        Guid ownerId, Guid tenantId, string resourceName, string? displayName, CancellationToken cancellationToken);

    Task<bool> DeleteAddressBookAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContactObject>> ListObjectsAsync(Guid collectionId, CancellationToken cancellationToken);

    Task<ContactObject?> GetObjectAsync(Guid collectionId, string resourceName, CancellationToken cancellationToken);

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

    Task<Calendar?> GetCalendarAsync(Guid ownerId, string resourceName, CancellationToken cancellationToken);

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

    Task<IReadOnlyList<CalendarObject>> GetCalendarObjectsAsync(
        Guid collectionId, IReadOnlyCollection<string> resourceNames, CancellationToken cancellationToken);

    /// <summary>
    /// Candidate objects for a time-range query: non-recurring objects overlapping the
    /// window plus all recurring objects (the caller expands those precisely). Null
    /// bounds mean an unbounded query (all live objects).
    /// </summary>
    Task<IReadOnlyList<CalendarObject>> QueryCalendarObjectsAsync(
        Guid collectionId, DateTime? startUtc, DateTime? endUtc, CancellationToken cancellationToken);

    Task<DavCalendarSyncResult> SyncCalendarAsync(Guid collectionId, long? sinceToken, CancellationToken cancellationToken);
}

public sealed record DavSyncResult(
    IReadOnlyList<ContactObject> Changed, IReadOnlyList<string> RemovedResourceNames, long Token);

public sealed record DavCalendarSyncResult(
    IReadOnlyList<CalendarObject> Changed, IReadOnlyList<string> RemovedResourceNames, long Token);
