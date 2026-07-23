namespace SimplCalCon.Api.Dav;

/// <summary>Opaque sync-token encoding a collection's change sequence (RFC 6578, ADR 0020).</summary>
public static class DavTokens
{
    private const string Prefix = "https://simplcalcon.example/ns/sync/";

    public static string Format(long changeSequence) => $"{Prefix}{changeSequence}";

    /// <summary>Parses a sync-token back to its change sequence; null if absent/foreign/malformed.</summary>
    public static long? TryParse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return long.TryParse(token.AsSpan(Prefix.Length), out var value) ? value : null;
    }
}
