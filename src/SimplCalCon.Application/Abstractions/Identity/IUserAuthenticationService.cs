using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Application.Abstractions.Identity;

/// <summary>
/// Validates a user's email + account password for the interactive (OIDC) login,
/// applying lockout on repeated failures (ADR 0018).
/// </summary>
public interface IUserAuthenticationService
{
    Task<UserAuthenticationResult> AuthenticateAsync(string email, string password, CancellationToken cancellationToken);
}

public enum UserAuthenticationStatus
{
    Success,
    InvalidCredentials,
    LockedOut,
    Disabled,
}

public sealed record UserAuthenticationResult(UserAuthenticationStatus Status, User? User)
{
    public static UserAuthenticationResult Success(User user) => new(UserAuthenticationStatus.Success, user);

    public static UserAuthenticationResult InvalidCredentials() => new(UserAuthenticationStatus.InvalidCredentials, null);

    public static UserAuthenticationResult LockedOut() => new(UserAuthenticationStatus.LockedOut, null);

    public static UserAuthenticationResult Disabled() => new(UserAuthenticationStatus.Disabled, null);
}
