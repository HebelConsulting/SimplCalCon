namespace SimplCalCon.Infrastructure.Configuration;

/// <summary>
/// First-run seeding (ADR 0016): the platform administrator (always, if none
/// exists) and an optional demo tenant + admin (Development only, for exercising
/// tenant-scoped sign-in).
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "SimplCalCon:Bootstrap";

    public PlatformAdminSeed? PlatformAdmin { get; set; }

    public DemoTenantSeed? DemoTenant { get; set; }
}

public sealed class PlatformAdminSeed
{
    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Platform Administrator";

    /// <summary>
    /// If set, the admin is created Active with this password. If omitted, the admin
    /// is created Invited and a one-time activation link is written to the logs.
    /// </summary>
    public string? Password { get; set; }
}

public sealed class DemoTenantSeed
{
    public string TenantName { get; set; } = "Demo";

    public string TenantSlug { get; set; } = "demo";

    public string AdminEmail { get; set; } = string.Empty;

    public string AdminDisplayName { get; set; } = "Demo Admin";

    public string? AdminPassword { get; set; }
}
