namespace SimplCalCon.Infrastructure.Configuration;

/// <summary>Account-lockout thresholds for interactive login (ADR 0018).</summary>
public sealed class LockoutOptions
{
    public const string SectionName = "SimplCalCon:Lockout";

    public int MaxFailedAccessAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);
}
