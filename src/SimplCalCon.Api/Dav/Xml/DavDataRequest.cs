using System.Globalization;
using System.Xml.Linq;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Dav.Xml;

/// <summary>
/// Parses a <c>calendar-data</c>/<c>address-data</c> request element (RFC 4791 §9.6 / RFC 6352
/// §10.4) into a provider-agnostic <see cref="CalendarDataRequest"/>/<see cref="AddressDataRequest"/>
/// so Infrastructure can subset + expand the blob (ADR 0054). Absent/empty element → the full object.
/// </summary>
internal static class DavDataRequest
{
    public static CalendarDataRequest ParseCalendarData(XElement? calendarData)
    {
        if (calendarData is null)
        {
            return CalendarDataRequest.Full;
        }

        var components = new Dictionary<string, DavCompSelection>(StringComparer.OrdinalIgnoreCase);
        foreach (var comp in calendarData.Elements(DavNames.CalComp))
        {
            CollectComp(comp, components);
        }

        var expandElement = calendarData.Element(DavNames.CalExpand);
        var expand = expandElement is not null
            && ParseUtc(expandElement.Attribute("start")?.Value) is { } start
            && ParseUtc(expandElement.Attribute("end")?.Value) is { } end
            ? new ExpandWindow(start, end)
            : null;

        return new CalendarDataRequest(components, expand);
    }

    public static AddressDataRequest ParseAddressData(XElement? addressData)
    {
        if (addressData is null)
        {
            return AddressDataRequest.Full;
        }

        var props = addressData.Elements(DavNames.CardProp)
            .Select(p => p.Attribute("name")?.Value)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new AddressDataRequest(props);
    }

    private static void CollectComp(XElement comp, Dictionary<string, DavCompSelection> components)
    {
        var name = comp.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var propElements = comp.Elements(DavNames.CalProp).ToList();
        var childComps = comp.Elements(DavNames.CalComp).ToList();
        var hasAllProp = comp.Element(DavNames.CalAllProp) is not null;
        var hasAllComp = comp.Element(DavNames.CalAllComp) is not null;

        // RFC 4791: a comp with no children returns all properties and all sub-components.
        var noChildren = propElements.Count == 0 && childComps.Count == 0 && !hasAllProp && !hasAllComp;

        var props = propElements
            .Select(p => p.Attribute("name")?.Value)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        components[name] = new DavCompSelection(hasAllProp || noChildren, hasAllComp || noChildren, props);

        foreach (var child in childComps)
        {
            CollectComp(child, components);
        }
    }

    private static DateTime? ParseUtc(string? value)
    {
        string[] formats = ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd"];
        return !string.IsNullOrEmpty(value)
            && DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }
}
