using System.Text;

namespace SimplCalCon.Client;

/// <summary>
/// Extracts the PHOTO from a vCard as a browser-renderable URL (ADR 0036): a base64
/// <c>PHOTO;ENCODING=b;TYPE=…</c> becomes a <c>data:</c> URL, a <c>data:</c>/URI value is
/// used as-is. Handles RFC 6350 line folding. Returns null when there's no usable photo.
/// </summary>
public static class VCardPhoto
{
    public static string? TryExtractDataUrl(string vcard)
    {
        if (string.IsNullOrEmpty(vcard))
        {
            return null;
        }

        foreach (var line in Unfold(vcard))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var segments = line[..colon].Split(';');
            if (!segments[0].Trim().Equals("PHOTO", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0)
            {
                return null;
            }

            // vCard 4.0 data URI, or a plain http(s) URL — usable directly.
            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            // vCard 3.0 inline base64: PHOTO;ENCODING=b;TYPE=JPEG:<base64>
            var parameters = segments.Skip(1).Select(p => p.Trim()).ToList();
            var isBase64 = parameters.Any(p =>
                p.Equals("ENCODING=b", StringComparison.OrdinalIgnoreCase)
                || p.Equals("ENCODING=BASE64", StringComparison.OrdinalIgnoreCase)
                || p.Equals("BASE64", StringComparison.OrdinalIgnoreCase));

            if (isBase64)
            {
                var type = parameters
                    .FirstOrDefault(p => p.StartsWith("TYPE=", StringComparison.OrdinalIgnoreCase))?["TYPE=".Length..]
                    ?? "jpeg";
                return $"data:image/{type.ToLowerInvariant()};base64,{value}";
            }

            return null;
        }

        return null;
    }

    // Join RFC 6350 folded continuation lines (those starting with a space or tab).
    private static IEnumerable<string> Unfold(string vcard)
    {
        var lines = vcard.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        var started = false;

        foreach (var line in lines)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                current.Append(line.AsSpan(1));
                continue;
            }

            if (started)
            {
                yield return current.ToString();
            }

            current.Clear();
            current.Append(line);
            started = true;
        }

        if (started)
        {
            yield return current.ToString();
        }
    }
}
