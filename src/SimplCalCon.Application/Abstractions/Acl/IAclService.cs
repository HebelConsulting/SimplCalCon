using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Application.Abstractions.Acl;

/// <summary>
/// Manages collection sharing grants and evaluates a user's effective rights on a
/// collection (ADR 0007): the union of their direct grants and grants to any group they
/// belong to transitively; the owner implicitly holds all rights.
/// </summary>
public interface IAclService
{
    /// <summary>Creates or replaces the grant for a principal on a collection (same tenant only).</summary>
    Task GrantAsync(Guid collectionId, Guid principalId, AclRight rights, CancellationToken cancellationToken);

    Task RevokeAsync(Guid collectionId, Guid principalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AclEntry>> ListGrantsAsync(Guid collectionId, CancellationToken cancellationToken);

    Task<AclRight> GetEffectiveRightsAsync(Guid userId, Guid collectionId, CancellationToken cancellationToken);
}
