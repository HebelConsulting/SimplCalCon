namespace SimplCalCon.Application.Abstractions.Security;

/// <summary>
/// Validates a candidate account password against the deployment's policy
/// (length-first with a common-password denylist; ADR 0018).
/// </summary>
public interface IPasswordPolicy
{
    PasswordPolicyResult Validate(string password);
}

/// <summary>Outcome of a password-policy check.</summary>
public sealed class PasswordPolicyResult
{
    private static readonly PasswordPolicyResult AcceptedResult = new(true, []);

    private PasswordPolicyResult(bool isAcceptable, IReadOnlyList<string> errors)
    {
        IsAcceptable = isAcceptable;
        Errors = errors;
    }

    public bool IsAcceptable { get; }

    public IReadOnlyList<string> Errors { get; }

    public static PasswordPolicyResult Accepted() => AcceptedResult;

    public static PasswordPolicyResult Rejected(IReadOnlyList<string> errors) => new(false, errors);
}
