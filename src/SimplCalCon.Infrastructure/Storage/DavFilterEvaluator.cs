using System.Text;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Evaluates parsed CalDAV/CardDAV query filters against an object's blob (ADR 0043). Lives in
/// Infrastructure so the Api stays free of blob parsing; reads properties at the line level with
/// RFC 5545/6350 unfolding, so any named property is filterable (not just the indexed columns).
/// </summary>
internal static class DavFilterEvaluator
{
    public static bool Matches(string blob, ContactQueryFilter filter) =>
        Matches(blob, filter.Test, filter.Props);

    // RFC 4791: prop-filters within a comp-filter are all required (allof).
    public static bool Matches(string blob, CalendarQueryFilter filter) =>
        Matches(blob, FilterTest.AllOf, filter.Props);

    private static bool Matches(string blob, FilterTest test, IReadOnlyList<DavPropFilter> props)
    {
        if (props.Count == 0)
        {
            return true;
        }

        return test == FilterTest.AllOf
            ? props.All(p => Matches(blob, p))
            : props.Any(p => Matches(blob, p));
    }

    private static bool Matches(string blob, DavPropFilter prop)
    {
        var values = PropertyValues(blob, prop.Name).ToList();
        if (prop.IsNotDefined)
        {
            return values.Count == 0;
        }

        if (prop.TextMatch is not { } match)
        {
            return values.Count > 0; // the property must merely be present
        }

        var any = values.Any(value => TextMatches(value, match));
        return match.Negate ? !any : any;
    }

    private static bool TextMatches(string value, DavTextMatch match)
    {
        const StringComparison caseless = StringComparison.OrdinalIgnoreCase; // default DAV collations are caseless
        return match.MatchType switch
        {
            TextMatchType.Equals => value.Equals(match.Value, caseless),
            TextMatchType.StartsWith => value.StartsWith(match.Value, caseless),
            TextMatchType.EndsWith => value.EndsWith(match.Value, caseless),
            _ => value.Contains(match.Value, caseless),
        };
    }

    // Values of a property in the unfolded blob, ignoring a group prefix ("item1.EMAIL") and parameters.
    private static IEnumerable<string> PropertyValues(string blob, string propertyName)
    {
        foreach (var line in Unfold(blob))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var nameField = line[..colon];
            var semicolon = nameField.IndexOf(';');
            if (semicolon >= 0)
            {
                nameField = nameField[..semicolon];
            }

            var dot = nameField.LastIndexOf('.');
            if (dot >= 0)
            {
                nameField = nameField[(dot + 1)..];
            }

            if (nameField.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                yield return Unescape(line[(colon + 1)..].Trim());
            }
        }
    }

    private static string Unescape(string value) => value
        .Replace("\\n", "\n").Replace("\\N", "\n").Replace("\\,", ",").Replace("\\;", ";");

    private static IEnumerable<string> Unfold(string blob)
    {
        var raw = blob.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var current = new StringBuilder();
        var has = false;
        foreach (var line in raw)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && has)
            {
                current.Append(line[1..]);
            }
            else
            {
                if (has)
                {
                    yield return current.ToString();
                }

                current.Clear();
                current.Append(line);
                has = true;
            }
        }

        if (has)
        {
            yield return current.ToString();
        }
    }
}
