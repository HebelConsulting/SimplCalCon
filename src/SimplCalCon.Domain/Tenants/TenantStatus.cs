namespace SimplCalCon.Domain.Tenants;

/// <summary>Lifecycle state of a <see cref="Tenant"/>.</summary>
public enum TenantStatus
{
    /// <summary>Fully operational.</summary>
    Active = 0,

    /// <summary>Retained but blocked: members cannot sign in or sync.</summary>
    Suspended = 1,
}
