namespace SimplCalCon.Domain.Authentication;

/// <summary>What a one-time <see cref="Token"/> authorizes.</summary>
public enum TokenPurpose
{
    /// <summary>First-time account activation: the invited user sets their password.</summary>
    Activation = 0,

    /// <summary>Resetting the password of an already-active account.</summary>
    PasswordReset = 1,
}
