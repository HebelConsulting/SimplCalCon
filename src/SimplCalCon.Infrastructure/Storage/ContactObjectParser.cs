using System.Text.RegularExpressions;
using FolkerKinzel.VCards;
using SimplCalCon.Domain.Objects.Exceptions;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>vCard parsing/extraction/splitting via FolkerKinzel.VCards (ADR 0003, 0004).</summary>
internal static partial class ContactObjectParser
{
    [GeneratedRegex(@"BEGIN:VCARD.*?END:VCARD", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex VCardBlock();

    public static ExtractedContact Parse(string blob, string uid)
    {
        VCard card;
        try
        {
            card = Vcf.Parse(blob).FirstOrDefault(c => c is not null)
                ?? throw new MalformedObjectException("No VCARD was found.");
        }
        catch (MalformedObjectException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MalformedObjectException(ex.Message);
        }

        var name = card.NameViews?.FirstOrDefault(n => n is not null)?.Value;

        return new ExtractedContact(
            uid,
            card.DisplayNames?.FirstOrDefault(d => d is not null)?.Value,
            name?.Surnames.FirstOrDefault(),
            name?.Given.FirstOrDefault(),
            card.Organizations?.FirstOrDefault(o => o is not null)?.Value?.Name,
            Join(card.EMails?.Where(e => e is not null).Select(e => e!.Value?.ToLowerInvariant())),
            Join(card.Phones?.Where(p => p is not null).Select(p => p!.Value)));
    }

    /// <summary>Splits a multi-card .vcf into one blob per card, preserving bytes.</summary>
    public static IEnumerable<string> Split(string content) =>
        VCardBlock().Matches(content).Select(m => m.Value);

    private static string? Join(IEnumerable<string?>? values)
    {
        if (values is null)
        {
            return null;
        }

        var joined = string.Join(';', values.Where(v => !string.IsNullOrWhiteSpace(v)));
        return joined.Length == 0 ? null : joined;
    }
}
