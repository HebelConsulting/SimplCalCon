using Microsoft.AspNetCore.Identity;

namespace SimplCalCon.Infrastructure.Security;

/// <summary>
/// Slow account/app-password hashing built on the framework's vetted
/// <see cref="PasswordHasher{TUser}"/> (PBKDF2). Used for both account passwords and
/// app-password secrets (ADR 0005, 0016).
/// </summary>
internal sealed class PasswordHashing
{
    private static readonly object HashSubject = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(HashSubject, password);

    public PasswordCheck Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(HashSubject, hash, password) switch
        {
            PasswordVerificationResult.Success => new PasswordCheck(true, false),
            PasswordVerificationResult.SuccessRehashNeeded => new PasswordCheck(true, true),
            _ => new PasswordCheck(false, false),
        };
}

internal readonly record struct PasswordCheck(bool Succeeded, bool NeedsRehash);
