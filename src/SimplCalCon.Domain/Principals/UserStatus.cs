namespace SimplCalCon.Domain.Principals;

/// <summary>Account state of a <see cref="User"/>.</summary>
public enum UserStatus
{
    /// <summary>Created but not yet activated; has an outstanding activation token and no usable password.</summary>
    Invited = 0,

    /// <summary>Activated and able to sign in.</summary>
    Active = 1,

    /// <summary>Blocked from signing in; retained for audit and possible re-enable.</summary>
    Disabled = 2,
}
