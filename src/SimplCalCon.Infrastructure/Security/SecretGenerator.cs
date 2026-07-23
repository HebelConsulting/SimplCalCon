using System.Security.Cryptography;

namespace SimplCalCon.Infrastructure.Security;

/// <summary>Generates high-entropy, URL-safe secrets (app-password secrets, tokens).</summary>
internal static class SecretGenerator
{
    public static string Create(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
