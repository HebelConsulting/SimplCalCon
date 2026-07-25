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
        var occurrences = PropertyOccurrences(blob, prop.Name).ToList();
        if (prop.IsNotDefined)
        {
            return occurrences.Count == 0;
        }

        // No param-filters: keep the value-only (collection-level negate) behaviour.
        if (prop.Params is not { Count: > 0 })
        {
            if (prop.TextMatch is not { } match)
            {
                return occurrences.Count > 0; // the property must merely be present
            }

            var any = occurrences.Any(o => TextMatches(o.Value, match));
            return match.Negate ? !any : any;
        }

        // With param-filters (RFC 4791/6352): some occurrence must satisfy the text-match AND every param-filter.
        return occurrences.Any(o =>
            ValueOk(o.Value, prop.TextMatch) && prop.Params.All(param => ParamMatches(o.Parameters, param)));
    }

    private static bool ValueOk(string value, DavTextMatch? match) =>
        match is null || (match.Negate ? !TextMatches(value, match) : TextMatches(value, match));

    private static bool ParamMatches(ILookup<string, string> parameters, DavParamFilter param)
    {
        var values = parameters[param.Name].ToList();
        if (param.IsNotDefined)
        {
            return values.Count == 0;
        }

        if (param.TextMatch is not { } match)
        {
            return values.Count > 0; // the parameter must merely be present
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

    // Occurrences of a property in the unfolded blob (value + parameters), ignoring a group prefix ("item1.EMAIL").
    private static IEnumerable<(string Value, ILookup<string, string> Parameters)> PropertyOccurrences(
        string blob, string propertyName)
    {
        foreach (var line in Unfold(blob))
        {
            var colon = FindValueColon(line);
            if (colon <= 0)
            {
                continue;
            }

            var nameField = line[..colon];
            var parts = SplitParams(nameField);
            var rawName = parts[0];
            var dot = rawName.LastIndexOf('.');
            if (dot >= 0)
            {
                rawName = rawName[(dot + 1)..];
            }

            if (!rawName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameters = parts.Skip(1)
                .Select(p => p.Split('=', 2))
                .Where(kv => kv.Length == 2)
                .SelectMany(kv => SplitValues(kv[1]).Select(v => (Key: kv[0], Value: v)))
                .ToLookup(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            yield return (Unescape(line[(colon + 1)..].Trim()), parameters);
        }
    }

    // The colon that starts the value — parameters may contain a quoted ':' which must be skipped.
    private static int FindValueColon(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (line[i] == ':' && !inQuotes)
            {
                return i;
            }
        }

        return -1;
    }

    // Split the name field on ';' outside quotes: "ATTENDEE;PARTSTAT=NEEDS-ACTION;CN=\"a;b\"".
    private static List<string> SplitParams(string nameField)
    {
        var parts = new List<string>();
        var inQuotes = false;
        var start = 0;
        for (var i = 0; i < nameField.Length; i++)
        {
            if (nameField[i] == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (nameField[i] == ';' && !inQuotes)
            {
                parts.Add(nameField[start..i]);
                start = i + 1;
            }
        }

        parts.Add(nameField[start..]);
        return parts;
    }

    // A parameter value may be a comma-separated list; quotes are stripped.
    private static IEnumerable<string> SplitValues(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => v.Trim('"'));

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
