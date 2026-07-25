namespace SimplCalCon.Application.Abstractions.Storage;

/// <summary>
/// A parsed CalDAV <c>calendar-data</c> request (RFC 4791 §9.6): which components/properties to
/// return and an optional recurrence <c>expand</c> window (ADR 0054). Empty + no expand = the full
/// object.
/// </summary>
public sealed record CalendarDataRequest(
    IReadOnlyDictionary<string, DavCompSelection> Components,
    ExpandWindow? Expand)
{
    public static readonly CalendarDataRequest Full = new(new Dictionary<string, DavCompSelection>(), null);

    /// <summary>Nothing to do — return the blob unchanged.</summary>
    public bool IsFull => Components.Count == 0 && Expand is null;
}

/// <summary>The properties/sub-components to keep for one component (RFC 4791 <c>comp</c>).</summary>
public sealed record DavCompSelection(bool AllProps, bool AllComps, IReadOnlySet<string> Props);

/// <summary>A recurrence-expansion window: return one component per occurrence starting in [Start, End).</summary>
public sealed record ExpandWindow(DateTime StartUtc, DateTime EndUtc);

/// <summary>A parsed CardDAV <c>address-data</c> request (RFC 6352 §10.4): which vCard properties to return.</summary>
public sealed record AddressDataRequest(IReadOnlySet<string> Props)
{
    public static readonly AddressDataRequest Full = new(new HashSet<string>());

    public bool IsFull => Props.Count == 0;
}

/// <summary>
/// Reduces a stored blob to a requested <c>calendar-data</c>/<c>address-data</c> subset and applies
/// recurrence expansion (ADR 0054). In Infrastructure so the Api stays free of iCal/vCard parsing.
/// </summary>
public interface IDavDataFormatter
{
    string FormatCalendar(string blob, CalendarDataRequest request);

    string FormatContact(string blob, AddressDataRequest request);
}
