using System.Text;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>
/// Reduces a stored iCal/vCard blob to a requested <c>calendar-data</c>/<c>address-data</c> subset
/// and applies recurrence <c>expand</c> (ADR 0054). Component/property selection is done at the line
/// level (preserving folding + unknown properties); expansion delegates to Ical.Net. VCALENDAR and
/// VTIMEZONE are always kept so the result stays a valid, timezone-resolvable object; UID and
/// RECURRENCE-ID are always kept so it stays identifiable.
/// </summary>
internal sealed class DavDataFormatter : IDavDataFormatter
{
    // UID/RECURRENCE-ID keep it identifiable; VERSION keeps the container valid.
    private static readonly HashSet<string> AlwaysKeepProps = new(StringComparer.OrdinalIgnoreCase) { "UID", "RECURRENCE-ID", "VERSION" };
    private static readonly HashSet<string> AlwaysKeepComps = new(StringComparer.OrdinalIgnoreCase) { "VCALENDAR", "VTIMEZONE", "VCARD" };

    public string FormatCalendar(string blob, CalendarDataRequest request)
    {
        if (request.IsFull)
        {
            return blob;
        }

        var working = request.Expand is { } window
            ? CalendarObjectParser.ExpandForData(blob, window.StartUtc, window.EndUtc)
            : blob;

        return request.Components.Count == 0 ? working : Subset(working, request.Components);
    }

    public string FormatContact(string blob, AddressDataRequest request) =>
        request.IsFull
            ? blob
            : Subset(blob, new Dictionary<string, DavCompSelection>(StringComparer.OrdinalIgnoreCase)
            {
                ["VCARD"] = new(AllProps: false, AllComps: true, request.Props),
            });

    // Walk the raw (folded) lines, keeping/dropping whole logical lines and whole components per the selection.
    private static string Subset(string blob, IReadOnlyDictionary<string, DavCompSelection> components)
    {
        var result = new StringBuilder();
        var stack = new Stack<(string Name, bool Included)>();
        var keepPreviousLogicalLine = true;

        foreach (var raw in blob.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            // A folded continuation line inherits the previous logical line's keep/drop decision.
            if (raw[0] is ' ' or '\t')
            {
                if (keepPreviousLogicalLine)
                {
                    result.Append(raw).Append("\r\n");
                }

                continue;
            }

            var name = LogicalName(raw);
            if (name.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                var comp = raw[(raw.IndexOf(':') + 1)..].Trim();
                var parentIncluded = stack.Count == 0 || stack.Peek().Included;
                var included = parentIncluded && ComponentIncluded(comp, stack, components);
                stack.Push((comp, included));
                keepPreviousLogicalLine = included;
            }
            else if (name.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                keepPreviousLogicalLine = stack.Count > 0 && stack.Peek().Included;
                if (stack.Count > 0)
                {
                    stack.Pop();
                }
            }
            else
            {
                keepPreviousLogicalLine = stack.Count > 0 && stack.Peek().Included && PropertyKept(name, stack.Peek().Name, components);
            }

            if (keepPreviousLogicalLine)
            {
                result.Append(raw).Append("\r\n");
            }
        }

        return result.ToString();
    }

    private static bool ComponentIncluded(
        string comp, Stack<(string Name, bool Included)> stack, IReadOnlyDictionary<string, DavCompSelection> components)
    {
        if (AlwaysKeepComps.Contains(comp) || components.ContainsKey(comp))
        {
            return true;
        }

        // Included when an ancestor selection allows all sub-components (or is itself unrestricted).
        return stack.Count > 0
            && components.TryGetValue(stack.Peek().Name, out var parent)
            && parent.AllComps;
    }

    private static bool PropertyKept(
        string propertyName, string componentName, IReadOnlyDictionary<string, DavCompSelection> components)
    {
        if (!components.TryGetValue(componentName, out var selection))
        {
            return true; // component kept but not explicitly restricted → all its props
        }

        return selection.AllProps
            || AlwaysKeepProps.Contains(propertyName)
            || selection.Props.Contains(propertyName);
    }

    // The property/component name of a content line: up to the first ';' or ':', with any group prefix stripped.
    private static string LogicalName(string line)
    {
        var end = line.Length;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] is ';' or ':')
            {
                end = i;
                break;
            }
        }

        var name = line[..end];
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }
}
