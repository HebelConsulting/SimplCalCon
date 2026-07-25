using System.Globalization;
using System.Xml.Linq;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Dav.Xml;

/// <summary>
/// Parses CalDAV <c>calendar-query</c> and CardDAV <c>addressbook-query</c> filters
/// (RFC 4791 §9.7 / RFC 6352 §10.5) into the provider-agnostic query model (ADR 0043).
/// v1: comp-filter component + time-range, prop-filter with text-match / is-not-defined, allof/anyof.
/// </summary>
internal static class DavFilterParser
{
    public static CalendarQueryFilter ParseCalendarQuery(XElement queryBody)
    {
        var filter = queryBody.Element(DavNames.CalFilter);
        var vcalendar = filter?.Element(DavNames.CompFilter);    // comp-filter name="VCALENDAR"
        var component = vcalendar?.Element(DavNames.CompFilter);  // comp-filter name="VEVENT" / "VTODO"
        var scope = component ?? vcalendar;

        var timeRange = scope?.Element(DavNames.TimeRange);
        var start = ParseIcalUtc(timeRange?.Attribute("start")?.Value);
        var end = ParseIcalUtc(timeRange?.Attribute("end")?.Value);

        var props = (scope?.Elements(DavNames.CalPropFilter) ?? Enumerable.Empty<XElement>())
            .Select(pf => ParsePropFilter(pf, DavNames.CalTextMatch, DavNames.CalIsNotDefined, caldav: true))
            .ToList();

        return new CalendarQueryFilter(component?.Attribute("name")?.Value, start, end, props);
    }

    public static ContactQueryFilter ParseAddressbookQuery(XElement queryBody)
    {
        var filter = queryBody.Element(DavNames.CardFilter);
        if (filter is null)
        {
            return ContactQueryFilter.MatchAll;
        }

        // RFC 6352: the filter test defaults to "anyof".
        var test = string.Equals(filter.Attribute("test")?.Value, "allof", StringComparison.OrdinalIgnoreCase)
            ? FilterTest.AllOf
            : FilterTest.AnyOf;

        var props = filter.Elements(DavNames.CardPropFilter)
            .Select(pf => ParsePropFilter(pf, DavNames.CardTextMatch, DavNames.CardIsNotDefined, caldav: false))
            .ToList();

        return new ContactQueryFilter(test, props);
    }

    private static DavPropFilter ParsePropFilter(XElement pf, XName textMatchName, XName isNotDefinedName, bool caldav)
    {
        var name = pf.Attribute("name")?.Value ?? string.Empty;
        if (pf.Element(isNotDefinedName) is not null)
        {
            return new DavPropFilter(name, IsNotDefined: true, TextMatch: null);
        }

        var textMatch = pf.Element(textMatchName);
        if (textMatch is null)
        {
            return new DavPropFilter(name, IsNotDefined: false, TextMatch: null);
        }

        var negate = string.Equals(textMatch.Attribute("negate-condition")?.Value, "yes", StringComparison.OrdinalIgnoreCase);
        // CalDAV text-match is always a substring match; CardDAV carries an explicit match-type.
        var matchType = caldav ? TextMatchType.Contains : ParseMatchType(textMatch.Attribute("match-type")?.Value);
        return new DavPropFilter(name, IsNotDefined: false, new DavTextMatch(textMatch.Value, matchType, negate));
    }

    private static TextMatchType ParseMatchType(string? value) => value?.ToLowerInvariant() switch
    {
        "equals" => TextMatchType.Equals,
        "starts-with" => TextMatchType.StartsWith,
        "ends-with" => TextMatchType.EndsWith,
        _ => TextMatchType.Contains,
    };

    private static DateTime? ParseIcalUtc(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        foreach (var format in (string[])["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd"])
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
        }

        return null;
    }
}
