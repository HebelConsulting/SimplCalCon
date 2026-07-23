namespace SimplCalCon.Application.Abstractions;

/// <summary>
/// Looks up users and groups within a tenant for the sharing UI's grantee picker
/// (ADR 0007). Tenant-scoped — never returns principals from another tenant.
/// </summary>
public interface IPrincipalDirectory
{
    Task<IReadOnlyList<PrincipalSummary>> SearchAsync(Guid tenantId, string? query, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrincipalSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public sealed record PrincipalSummary(Guid Id, string Kind, string DisplayName, string? Email);
