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

        // expand flattens to occurrences (and already bounds the range); limit-recurrence-set keeps the
        // master + only in-range overrides (RFC 4791 §9.6.5). They're alternatives — expand wins.
        var working = request.Expand is { } window
            ? CalendarObjectParser.ExpandForData(blob, window.StartUtc, window.EndUtc)
            : request.Limit is { } limit
                ? CalendarObjectParser.LimitRecurrenceSet(blob, limit.StartUtc, limit.EndUtc)
                : blob;

        return request.Root is null ? working : Subset(working, request.Root);
    }

    public string FormatContact(string blob, AddressDataRequest request) =>
        request.IsFull
            ? blob
            // The top VCARD keeps only the requested properties (+ always-keep); vCards have no sub-components.
            : Subset(blob, new DavCompSelection(AllProps: false, AllComps: true, request.Props, EmptyComps));

    private static readonly IReadOnlyDictionary<string, DavCompSelection> EmptyComps =
        new Dictionary<string, DavCompSelection>();

    // Walk the raw (folded) lines, keeping/dropping whole logical lines and whole components per the
    // selection tree (ADR 0073). Each stack frame carries the selection node for that component; a null
    // Selection means "keep everything below" (allcomp / an always-kept component like VTIMEZONE).
    private static string Subset(string blob, DavCompSelection root)
    {
        var result = new StringBuilder();
        var stack = new Stack<(bool Included, DavCompSelection? Selection)>();
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
                var frame = ResolveComp(comp, stack, root);
                stack.Push(frame);
                keepPreviousLogicalLine = frame.Included;
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
                var top = stack.Count > 0 ? stack.Peek() : (Included: true, Selection: (DavCompSelection?)root);
                keepPreviousLogicalLine = top.Included && PropertyKept(name, top.Selection);
            }

            if (keepPreviousLogicalLine)
            {
                result.Append(raw).Append("\r\n");
            }
        }

        return result.ToString();
    }

    // Decide whether a component is kept and which selection node governs it (and its children).
    private static (bool Included, DavCompSelection? Selection) ResolveComp(
        string comp, Stack<(bool Included, DavCompSelection? Selection)> stack, DavCompSelection root)
    {
        if (stack.Count == 0)
        {
            return (true, root); // the top component (VCALENDAR / VCARD) — the request root governs it
        }

        var parent = stack.Peek();
        if (!parent.Included)
        {
            return (false, null);
        }

        if (parent.Selection is not { } parentSel)
        {
            return (true, null); // parent is keep-all → keep this too, keep-all
        }

        if (parentSel.Comps.TryGetValue(comp, out var childSel))
        {
            return (true, childSel);
        }

        // allcomp, or an always-kept component (VTIMEZONE keeps the object valid) → keep it, keep-all.
        return parentSel.AllComps || AlwaysKeepComps.Contains(comp) ? (true, null) : (false, null);
    }

    // null selection = keep-all; otherwise keep by allprop / always-keep / explicit prop.
    private static bool PropertyKept(string propertyName, DavCompSelection? selection) =>
        selection is null
        || selection.AllProps
        || AlwaysKeepProps.Contains(propertyName)
        || selection.Props.Contains(propertyName);

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
