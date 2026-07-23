using System.Diagnostics.CodeAnalysis;

namespace SimplCalCon.Api.Http;

/// <summary>Formats and parses strong ETags backed by a concurrency-token <see cref="Guid"/>.</summary>
public static class ETag
{
    /// <summary>Renders a token as a strong, quoted ETag: <c>"guid"</c>.</summary>
    public static string Format(Guid token) => $"\"{token:D}\"";

    /// <summary>
    /// Parses an <c>If-Match</c> value into its concurrency token. Returns false for a
    /// malformed value; a caller treating that as a mismatch yields 412.
    /// </summary>
    public static bool TryParse(string? value, out Guid token)
    {
        token = Guid.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("W/", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        trimmed = trimmed.Trim('"');
        return Guid.TryParse(trimmed, out token);
    }

    /// <summary>True when the value is the wildcard <c>*</c> (match any current representation).</summary>
    public static bool IsWildcard([NotNullWhen(true)] string? value) => value?.Trim() == "*";
}
