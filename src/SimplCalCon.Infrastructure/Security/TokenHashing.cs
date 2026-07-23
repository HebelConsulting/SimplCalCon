using System.Security.Cryptography;
using System.Text;

namespace SimplCalCon.Infrastructure.Security;

/// <summary>
/// Fast one-way hash for high-entropy secrets that don't need a slow hash: the
/// stored form of activation/reset tokens, and the DAV fast-verify cache key. A
/// plain SHA-256 is sufficient because the inputs are random 256-bit secrets.
/// </summary>
internal static class TokenHashing
{
    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
