using System.ComponentModel.DataAnnotations;

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

/// <summary>Rename a collection's display name (ADR 0041).</summary>
public sealed class CollectionRenameRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }
}

/// <summary>Move an entry into another collection of the same kind (ADR 0042).</summary>
public sealed class MoveObjectRequest
{
    [Required]
    public required Guid TargetId { get; init; }
}

public sealed class EventWriteRequest
{
    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public required string Summary { get; init; }

    public required DateTime StartUtc { get; init; }

    public DateTime? EndUtc { get; init; }

    public bool IsAllDay { get; init; }

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
