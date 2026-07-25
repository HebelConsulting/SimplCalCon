namespace SimplCalCon.Api.Contracts;

/// <summary>A tenant, for the platform-admin view (ADR 0034).</summary>
public sealed record TenantResource(Guid Id, string Name, string Slug, string Status);

/// <summary>A user within a tenant, for the tenant-admin view (ADR 0034).</summary>
public sealed record AdminUserResource(Guid Id, string DisplayName, string Email, string Role, string Status);

/// <summary>A tenant's SMTP/iMIP settings for the tenant-admin view (ADR 0047); the password is never returned.</summary>
public sealed record TenantEmailSettingsResource(
    bool Enabled, string Host, int Port, bool UseStartTls, string? Username, bool HasPassword, string FromAddress, string? FromName);

/// <summary>Write a tenant's SMTP settings (ADR 0047). NewPassword: null keeps the stored one, "" clears it.</summary>
public sealed class TenantEmailSettingsWriteRequest
{
    public bool Enabled { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public bool UseStartTls { get; init; } = true;

    public string? Username { get; init; }

    public string? NewPassword { get; init; }

    public string FromAddress { get; init; } = string.Empty;

    public string? FromName { get; init; }
}
