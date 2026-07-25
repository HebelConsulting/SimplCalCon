namespace SimplCalCon.Client.Models;

public sealed record Collection<T>(IReadOnlyList<T> Items);

public sealed record MeDto(Guid Id, string Email, string DisplayName, Guid? TenantId, string Role, bool HasPhoto = false)
{
    public bool IsAdmin => Role is "platform_admin" or "admin";
    public bool IsPlatformAdmin => Role is "platform_admin";

    public string Initials
    {
        get
        {
            var parts = (DisplayName ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
                _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant(),
            };
        }
    }
}

public sealed record CalendarDto(Guid Id, string Name, string? Color, bool SupportsEvents, bool SupportsTasks, bool Shared);

public sealed record AddressBookDto(Guid Id, string Name, bool Shared);

public sealed record EventDto(
    Guid Id, string? Summary, DateTime? StartUtc, DateTime? EndUtc, bool IsAllDay, bool IsRecurring,
    IReadOnlyList<AttendeeDto>? Attendees = null, string? Location = null,
    RecurrenceDto? Recurrence = null, string? RecurrenceRule = null, bool RecurrenceSupported = false);

/// <summary>A structured repeat rule the editor can model (ADR 0050).</summary>
public sealed record RecurrenceDto(
    string Frequency, int Interval, IReadOnlyList<string> ByDay, int? Count, DateTime? UntilUtc);

public sealed record AttendeeDto(string Address, string? CommonName, string Role, string ParticipationStatus, bool IsOrganizer);

public sealed record FreeBusyDto(string Address, DateTime FromUtc, DateTime ToUtc, bool Resolved, IReadOnlyList<BusyPeriodDto> Busy);

public sealed record BusyPeriodDto(DateTime StartUtc, DateTime EndUtc);

public sealed record ContactDto(
    Guid Id, string? FormattedName, string? Organization, IReadOnlyList<string> Emails, IReadOnlyList<string> Phones,
    bool HasPhoto = false);

/// <summary>RFC 7807 problem details (the fields the UI surfaces).</summary>
public sealed record ProblemDto(string? Title, string? Detail, int? Status, string? ErrorCode);

public sealed record AppPasswordDto(Guid Id, string Label, DateTime CreatedAt, DateTime? LastUsedAt);

public sealed record CreatedAppPassword(Guid Id, string Label, string Secret);

public sealed record ShareDto(Guid PrincipalId, string Kind, string DisplayName, string? Email, IReadOnlyList<string> Rights);
public sealed record SharedCollectionDto(Guid Id, string Kind, string Name, string OwnerName, IReadOnlyList<string> Rights);

public sealed record PrincipalDto(Guid Id, string Kind, string DisplayName, string? Email);
public sealed record TenantEmailSettingsDto(
    bool Enabled, string Host, int Port, bool UseStartTls, string? Username, bool HasPassword, string FromAddress, string? FromName);

public sealed record TrashItemDto(Guid Id, string? Summary, string? FormattedName, DateTime? DeletedAt)
{
    public string Title => Summary ?? FormattedName ?? "(untitled)";
}

public sealed record RevisionDto(long RevisionNumber, string Operation, DateTime CreatedAt, Guid? AuthorPrincipalId);

public sealed record ImportResultDto(
    int Imported, int Skipped, int Failed, IReadOnlyList<string> Errors, int CreatedCollections = 0);

public sealed record InvitationDto(
    string ResourceName, string Uid, string? Summary, DateTime? StartUtc, DateTime? EndUtc,
    string OrganizerEmail, string? OrganizerName);
public sealed record InvitationCountDto(int Count);

public sealed record TakeoutImportResultDto(
    int CollectionsCreated, int Imported, int Skipped, int Failed, IReadOnlyList<string> Errors);

public sealed record TenantDto(Guid Id, string Name, string Slug, string Status);

public sealed record AdminUserDto(Guid Id, string DisplayName, string Email, string Role, string Status);
