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

public sealed class EventWriteRequest
{
    [Required]
    [StringLength(1024, MinimumLength = 1)]
    public required string Summary { get; init; }

    public required DateTime StartUtc { get; init; }

    public DateTime? EndUtc { get; init; }

    public bool IsAllDay { get; init; }
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
