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

public sealed record TrashItemDto(Guid Id, string? Summary, string? FormattedName, DateTime? DeletedAt)
{
    public string Title => Summary ?? FormattedName ?? "(untitled)";
}

public sealed record RevisionDto(long RevisionNumber, string Operation, DateTime CreatedAt, Guid? AuthorPrincipalId);

public sealed record ImportResultDto(int Imported, int Skipped, int Failed, IReadOnlyList<string> Errors);

public sealed record TakeoutImportResultDto(
    int CollectionsCreated, int Imported, int Skipped, int Failed, IReadOnlyList<string> Errors);
