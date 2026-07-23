namespace SimplCalCon.Infrastructure.Configuration;

/// <summary>Length-first account-password policy settings (ADR 0018).</summary>
public sealed class PasswordPolicyOptions
{
    public const string SectionName = "SimplCalCon:PasswordPolicy";

    public int MinimumLength { get; set; } = 12;

    /// <summary>Case-insensitive exact-match denylist of common passwords to reject.</summary>
    public HashSet<string> Denylist { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "12345678", "123456789", "qwertyuiop", "changeme", "letmein",
    };
}
