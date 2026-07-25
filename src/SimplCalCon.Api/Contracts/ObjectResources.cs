using System.Text.Json.Serialization;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Contracts;

/// <summary>A calendar event (extracted fields; the raw blob is available via DAV).</summary>
public sealed class EventResource : HypermediaResource, IETaggedResource
{
    public required Guid Id { get; init; }

    public required string ResourceName { get; init; }

    public string? Summary { get; init; }

    public string? Location { get; init; }

    public DateTime? StartUtc { get; init; }

    public DateTime? EndUtc { get; init; }

    public bool IsAllDay { get; init; }

    public bool IsRecurring { get; init; }

    /// <summary>Structured repeat rule when the editor can model it (ADR 0050); null for none or a custom rule.</summary>
    public Recurrence? Recurrence { get; init; }

    /// <summary>The raw RRULE value (any rule), null when not recurring.</summary>
    public string? RecurrenceRule { get; init; }

    /// <summary>True when <see cref="Recurrence"/> models the rule; false means it's shown read-only (custom).</summary>
    public bool RecurrenceSupported { get; init; }

    /// <summary>Organizer + attendees (ADR 0030); the organizer is the entry with <c>isOrganizer</c>.</summary>
    public IReadOnlyList<AttendeeResource> Attendees { get; init; } = [];

    /// <summary>Set only when the event is in the trash (ADR 0028); null otherwise.</summary>
    public DateTime? DeletedAt { get; init; }

    [JsonIgnore]
    public Guid ConcurrencyToken { get; init; }
}

/// <summary>An ORGANIZER/ATTENDEE of an event (ADR 0030).</summary>
public sealed class AttendeeResource
{
    public required string Address { get; init; }

    public string? CommonName { get; init; }

    public required string Role { get; init; }

    public required string ParticipationStatus { get; init; }

    public bool IsOrganizer { get; init; }
}

/// <summary>The result of splitting an event: the truncated original and the newly created tail copy (ADR 0027).</summary>
public sealed class SplitEventResource : HypermediaResource
{
    public required EventResource Original { get; init; }

    public required EventResource Created { get; init; }
}

/// <summary>A contact (extracted fields).</summary>
public sealed class ContactResource : HypermediaResource, IETaggedResource
{
    public required Guid Id { get; init; }

    public required string ResourceName { get; init; }

    public string? FormattedName { get; init; }

    public string? FamilyName { get; init; }

    public string? GivenName { get; init; }

    public string? Organization { get; init; }

    public IReadOnlyList<string> Emails { get; init; } = [];

    public IReadOnlyList<string> Phones { get; init; } = [];

    /// <summary>Whether the card carries a PHOTO property (for the "with photos" filter, ADR 0036).</summary>
    public bool HasPhoto { get; init; }

    /// <summary>Set only when the contact is in the trash (ADR 0028); null otherwise.</summary>
    public DateTime? DeletedAt { get; init; }

    [JsonIgnore]
    public Guid ConcurrencyToken { get; init; }
}

/// <summary>One prior state of an object in its version history (ADR 0011/0028).</summary>
public sealed class RevisionResource : HypermediaResource
{
    public required long RevisionNumber { get; init; }

    /// <summary>Created, Updated, Deleted, or Restored.</summary>
    public required string Operation { get; init; }

    public required DateTime CreatedAt { get; init; }

    public Guid? AuthorPrincipalId { get; init; }
}
