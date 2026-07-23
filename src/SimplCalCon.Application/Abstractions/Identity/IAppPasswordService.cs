using SimplCalCon.Domain.Authentication;

namespace SimplCalCon.Application.Abstractions.Identity;

/// <summary>
/// Issues per-device DAV app passwords (ADR 0005). The raw secret is returned once
/// at creation and never persisted in clear; verification is
/// <see cref="IDavCredentialAuthenticator"/>.
/// </summary>
public interface IAppPasswordService
{
    Task<IssuedAppPassword> IssueAsync(Guid userId, string label, CancellationToken cancellationToken);

    /// <summary>The user's active (non-revoked) app passwords, newest first.</summary>
    Task<IReadOnlyList<AppPassword>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A single active app password owned by the user, or null.</summary>
    Task<AppPassword?> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes an active app password. <paramref name="expectedConcurrencyToken"/> is the
    /// If-Match token (null means the wildcard <c>*</c>); a stale value surfaces as a
    /// concurrency failure. Returns false if no matching active credential exists.
    /// </summary>
    Task<bool> RevokeAsync(
        Guid userId, Guid id, Guid? expectedConcurrencyToken, CancellationToken cancellationToken);
}

/// <summary>A freshly created app password and its one-time clear-text secret.</summary>
public sealed record IssuedAppPassword(AppPassword AppPassword, string Secret);
