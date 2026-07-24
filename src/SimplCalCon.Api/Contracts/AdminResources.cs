namespace SimplCalCon.Api.Contracts;

/// <summary>A tenant, for the platform-admin view (ADR 0034).</summary>
public sealed record TenantResource(Guid Id, string Name, string Slug, string Status);

/// <summary>A user within a tenant, for the tenant-admin view (ADR 0034).</summary>
public sealed record AdminUserResource(Guid Id, string DisplayName, string Email, string Role, string Status);
