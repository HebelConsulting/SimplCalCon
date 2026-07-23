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
}

/// <summary>A freshly created app password and its one-time clear-text secret.</summary>
public sealed record IssuedAppPassword(AppPassword AppPassword, string Secret);
