using System.Text;
using System.Text.RegularExpressions;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Line-level helpers for iCalendar/vCard payloads that don't need a full parser:
/// UID extraction (robust across parser-library versions) and UID injection.
/// </summary>
internal static partial class BlobText
{
    [GeneratedRegex(@"^UID:(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex UidLine();

    /// <summary>The first UID value in the payload, unfolded, or null if none.</summary>
    public static string? ExtractUid(string blob)
    {
        var match = UidLine().Match(Unfold(blob));
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Ensures the vCard carries a UID, injecting a generated one before END:VCARD when
    /// absent (vCard 3.0 makes UID optional). Returns the (possibly rewritten) blob and
    /// the effective UID.
    /// </summary>
    public static (string Blob, string Uid) EnsureVCardUid(string blob)
    {
        var existing = ExtractUid(blob);
        if (!string.IsNullOrEmpty(existing))
        {
            return (blob, existing);
        }

        var uid = Guid.NewGuid().ToString();
        var newline = blob.Contains("\r\n") ? "\r\n" : "\n";
        var injected = Regex.Replace(
            blob,
            @"(?i)(\r?\n)(END:VCARD)",
            $"{newline}UID:{uid}$1$2",
            RegexOptions.None);

        return (injected, uid);
    }

    // Undo RFC 5545/6350 line folding (a CRLF followed by a space/tab continues the line).
    private static string Unfold(string blob)
    {
        var builder = new StringBuilder(blob.Length);
        var lines = blob.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                builder.Append(line.AsSpan(1));
            }
            else
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(line);
            }
        }

        return builder.ToString();
    }
}
