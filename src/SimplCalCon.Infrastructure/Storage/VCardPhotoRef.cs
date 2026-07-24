using System.Text;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Extracts the PHOTO property from a vCard blob (ADR 0037) as a discriminated reference: an
/// inline (base64 / data-URI) photo the card already carries, an external URL to fetch, or none.
/// Also rewrites a card's PHOTO to an inline base64 photo (used when embedding a cached copy).
/// Works at the line level after RFC 6350 unfolding — it never round-trips through a vCard parser.
/// </summary>
public abstract record VCardPhotoRef
{
    public sealed record None : VCardPhotoRef;

    public sealed record Inline(byte[] Bytes, string ContentType) : VCardPhotoRef;

    public sealed record Url(string Value) : VCardPhotoRef;

    public static VCardPhotoRef Parse(string vcard)
    {
        var line = FindPhotoLine(vcard);
        if (line is null)
        {
            return new None();
        }

        var (parameters, value) = SplitProperty(line);
        value = value.Trim();
        if (value.Length == 0)
        {
            return new None();
        }

        // data: URI — an inline photo expressed as a URL (RFC 6350 §6.2 / vCard 4.0 style).
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataUri(value);
        }

        // External reference (vCard 4.0 uses a plain URI value).
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new Url(value);
        }

        // vCard 3.0 inline binary: PHOTO;ENCODING=b;TYPE=JPEG:<base64>.
        if (IsBase64Encoded(parameters))
        {
            try
            {
                var bytes = Convert.FromBase64String(StripWhitespace(value));
                return bytes.Length == 0 ? new None() : new Inline(bytes, ContentTypeFromParameters(parameters));
            }
            catch (FormatException)
            {
                return new None();
            }
        }

        return new None();
    }

    /// <summary>
    /// Returns <paramref name="vcard"/> with any existing PHOTO property replaced by an inline
    /// base64 photo. The card's line endings are normalized to CRLF.
    /// </summary>
    public static string ReplacePhoto(string vcard, byte[] bytes, string contentType)
    {
        var subtype = contentType.Contains('/', StringComparison.Ordinal)
            ? contentType.Split('/')[1].ToUpperInvariant()
            : "JPEG";
        var photoLine = $"PHOTO;ENCODING=b;TYPE={subtype}:{Convert.ToBase64String(bytes)}";

        var lines = vcard.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var result = new StringBuilder();
        var skippingFolded = false;
        var inserted = false;

        foreach (var line in lines)
        {
            if (skippingFolded)
            {
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                {
                    continue; // folded continuation of the dropped PHOTO
                }

                skippingFolded = false;
            }

            if (IsPhotoLine(line))
            {
                skippingFolded = true; // drop the old PHOTO (and its folded continuation lines)
                continue;
            }

            if (!inserted && line.Trim().Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                result.Append(photoLine).Append("\r\n");
                inserted = true;
            }

            if (line.Length == 0)
            {
                continue;
            }

            result.Append(line).Append("\r\n");
        }

        return result.ToString();
    }

    private static VCardPhotoRef ParseDataUri(string value)
    {
        var comma = value.IndexOf(',');
        if (comma < 0)
        {
            return new None();
        }

        var meta = value[5..comma]; // between "data:" and ","
        var data = value[(comma + 1)..];
        if (!meta.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            return new None();
        }

        var contentType = meta.Split(';')[0];
        contentType = contentType.Length == 0 ? "image/jpeg" : contentType;
        try
        {
            var bytes = Convert.FromBase64String(StripWhitespace(data));
            return bytes.Length == 0 ? new None() : new Inline(bytes, contentType);
        }
        catch (FormatException)
        {
            return new None();
        }
    }

    private static string? FindPhotoLine(string vcard)
    {
        var lines = Unfold(vcard);
        return lines.FirstOrDefault(IsPhotoLine);
    }

    // RFC 6350 §3.2: a line beginning with a space or tab continues the previous line.
    private static List<string> Unfold(string vcard)
    {
        var raw = vcard.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var unfolded = new List<string>();
        foreach (var line in raw)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && unfolded.Count > 0)
            {
                unfolded[^1] += line[1..];
            }
            else
            {
                unfolded.Add(line);
            }
        }

        return unfolded;
    }

    private static bool IsPhotoLine(string line)
    {
        // The property name may carry a group prefix ("item1.PHOTO") and is terminated by ';' or ':'.
        var nameEnd = line.IndexOfAny([';', ':']);
        if (nameEnd < 0)
        {
            return false;
        }

        var name = line[..nameEnd];
        var dot = name.LastIndexOf('.');
        if (dot >= 0)
        {
            name = name[(dot + 1)..];
        }

        return name.Equals("PHOTO", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Parameters, string Value) SplitProperty(string line)
    {
        var colon = line.IndexOf(':');
        return colon < 0 ? (line, string.Empty) : (line[..colon], line[(colon + 1)..]);
    }

    private static bool IsBase64Encoded(string parameters) =>
        parameters.Contains("ENCODING=b", StringComparison.OrdinalIgnoreCase)
        || parameters.Contains("ENCODING=BASE64", StringComparison.OrdinalIgnoreCase)
        || parameters.Contains("BASE64", StringComparison.OrdinalIgnoreCase);

    private static string ContentTypeFromParameters(string parameters)
    {
        foreach (var part in parameters.Split(';'))
        {
            var eq = part.IndexOf('=');
            if (eq > 0 && part[..eq].Trim().Equals("TYPE", StringComparison.OrdinalIgnoreCase))
            {
                var type = part[(eq + 1)..].Trim().Trim('"').ToLowerInvariant();
                if (type.Length > 0)
                {
                    return type.Contains('/', StringComparison.Ordinal) ? type : $"image/{type}";
                }
            }
        }

        return "image/jpeg";
    }

    private static string StripWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
