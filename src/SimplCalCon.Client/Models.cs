namespace SimplCalCon.Client.Models;

public sealed record Collection<T>(IReadOnlyList<T> Items);

public sealed record CalendarDto(Guid Id, string Name, string? Color, bool SupportsEvents, bool SupportsTasks, bool Shared);

public sealed record AddressBookDto(Guid Id, string Name, bool Shared);

public sealed record EventDto(Guid Id, string? Summary, DateTime? StartUtc, DateTime? EndUtc, bool IsAllDay, bool IsRecurring);

public sealed record ContactDto(
    Guid Id, string? FormattedName, string? Organization, IReadOnlyList<string> Emails, IReadOnlyList<string> Phones);

public sealed record AppPasswordDto(Guid Id, string Label, DateTime CreatedAt, DateTime? LastUsedAt);

public sealed record CreatedAppPassword(Guid Id, string Label, string Secret);

public sealed record ShareDto(Guid PrincipalId, string Kind, string DisplayName, string? Email, IReadOnlyList<string> Rights);

public sealed record PrincipalDto(Guid Id, string Kind, string DisplayName, string? Email);
