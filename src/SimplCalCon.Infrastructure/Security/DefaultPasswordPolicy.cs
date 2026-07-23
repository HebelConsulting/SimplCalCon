using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions.Security;
using SimplCalCon.Infrastructure.Configuration;

namespace SimplCalCon.Infrastructure.Security;

internal sealed class DefaultPasswordPolicy(IOptions<PasswordPolicyOptions> options) : IPasswordPolicy
{
    private readonly PasswordPolicyOptions _options = options.Value;

    public PasswordPolicyResult Validate(string password)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password) || password.Length < _options.MinimumLength)
        {
            errors.Add($"Password must be at least {_options.MinimumLength} characters long.");
        }

        if (!string.IsNullOrEmpty(password) && _options.Denylist.Contains(password))
        {
            errors.Add("Password is too common; choose a less predictable password.");
        }

        return errors.Count == 0
            ? PasswordPolicyResult.Accepted()
            : PasswordPolicyResult.Rejected(errors);
    }
}
