using System.ComponentModel.DataAnnotations;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Contracts;

public sealed class CalendarCreateRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    public string? Color { get; init; }

    public bool SupportsEvents { get; init; } = true;

    public bool SupportsTasks { get; init; } = true;
}

public sealed class AddressBookCreateRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }
}

/// <summary>Update a collection's display name and colour (ADR 0041/0062).</summary>
public sealed class CollectionUpdateRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>Hex colour (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>); null clears it (the UI auto-assigns).</summary>
    [RegularExpression("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", ErrorMessage = "Color must be a hex value like #RRGGBB.")]
    public string? Color { get; init; }
}

/// <summary>Move an entry into another collection of the same kind (ADR 0042).</summary>
public sealed class MoveObjectRequest
{
    [Required]
    public required Guid TargetId { get; init; }
}

/// <summary>Respond to a schedule-inbox invitation (ADR 0045).</summary>
public sealed class InvitationRespondRequest
{
    [Required]
    public required string ResourceName { get; init; }

    /// <summary>accepted | tentative | declined.</summary>
    [Required]
    public required string Response { get; init; }
}

public sealed class EventWriteRequest
{
    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public required string Summary { get; init; }

    public required DateTime StartUtc { get; init; }

    public DateTime? EndUtc { get; init; }

    public bool IsAllDay { get; init; }

    [StringLength(1024)]
    public string? Location { get; init; }

    /// <summary>Structured repeat rule (ADR 0050); null = does not repeat (or use <see cref="RecurrenceRule"/> to preserve).</summary>
    public Recurrence? Recurrence { get; init; }

    /// <summary>A raw RRULE value to preserve verbatim when the rule is too complex for the structured editor (ADR 0050).</summary>
    [StringLength(1024)]
    public string? RecurrenceRule { get; init; }

    /// <summary>Organizer calendar-user address (ADR 0030); defaults to the caller when attendees are present.</summary>
    public string? Organizer { get; init; }

    public IReadOnlyList<AttendeeWriteRequest> Attendees { get; init; } = [];
}

public sealed class AttendeeWriteRequest
{
    [Required]
    [StringLength(320, MinimumLength = 1)]
    public required string Address { get; init; }

    public string? CommonName { get; init; }
}

/// <summary>Soft-delete several objects in one call (ADR 0055). If-Match-exempt (operates on current versions).</summary>
public sealed class BulkDeleteRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
}

/// <summary>Move several objects to another collection in one call (ADR 0055).</summary>
public sealed class BulkMoveRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];

    public Guid TargetId { get; init; }
}

public sealed class SplitEventRequest
{
    /// <summary>The instant (UTC) at which to split: the original ends here, the copy starts here.</summary>
    public required DateTime AtUtc { get; init; }
}

public sealed class ContactWriteRequest
{
    public string? FormattedName { get; init; }

    public string? FamilyName { get; init; }

    public string? GivenName { get; init; }

    public string? Organization { get; init; }

    public IReadOnlyList<string> Emails { get; init; } = [];

    public IReadOnlyList<string> Phones { get; init; } = [];
}
