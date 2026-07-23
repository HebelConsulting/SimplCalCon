namespace SimplCalCon.Application.Abstractions.Identity;

/// <summary>
/// Verifies DAV Basic credentials (email + app-password secret) on every sync
/// request. Implementations use a slow hash at rest plus a short-lived fast-verify
/// cache so repeated polling doesn't re-run the slow hash (ADR 0005).
/// </summary>
public interface IDavCredentialAuthenticator
{
    Task<DavIdentity?> AuthenticateAsync(string email, string secret, CancellationToken cancellationToken);
}

/// <summary>The identity established by a successful DAV credential check.</summary>
public sealed record DavIdentity(Guid UserId, Guid? TenantId, string Email, Guid AppPasswordId);
