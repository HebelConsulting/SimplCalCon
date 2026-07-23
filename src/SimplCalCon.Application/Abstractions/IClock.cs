namespace SimplCalCon.Application.Abstractions;

/// <summary>Abstracts the current time so time-dependent logic is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
