using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Infrastructure.Storage;

namespace SimplCalCon.UnitTests;

public sealed class DavFilterEvaluatorTests
{
    private const string Vcard =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Jane Doe\r\nORG:Acme\r\n" +
        "EMAIL;TYPE=WORK:jane@work.example\r\nEMAIL;TYPE=HOME:jane@home.example\r\nTEL;TYPE=CELL:+15550001\r\nEND:VCARD\r\n";

    private const string Event =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:e1\r\nSUMMARY:Team Meeting\r\n" +
        "ATTENDEE;PARTSTAT=NEEDS-ACTION;CN=Bob:mailto:bob@example\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static DavTextMatch Contains(string v, bool negate = false) => new(v, TextMatchType.Contains, negate);

    [Fact]
    public void Text_match_contains_matches_present_value()
    {
        var filter = new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("FN", false, Contains("jane"))]);
        Assert.True(DavFilterEvaluator.Matches(Vcard, filter));
        Assert.False(DavFilterEvaluator.Matches(Vcard, new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("FN", false, Contains("zzz"))])));
    }

    [Theory]
    [InlineData(TextMatchType.Equals, "jane@work.example", true)]
    [InlineData(TextMatchType.Equals, "jane@work", false)]
    [InlineData(TextMatchType.StartsWith, "jane@work", true)]
    [InlineData(TextMatchType.EndsWith, "home.example", true)]
    [InlineData(TextMatchType.StartsWith, "home", false)]
    public void Text_match_modes(TextMatchType mode, string value, bool expected)
    {
        var filter = new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("EMAIL", false, new DavTextMatch(value, mode, false))]);
        Assert.Equal(expected, DavFilterEvaluator.Matches(Vcard, filter));
    }

    [Fact]
    public void Negate_inverts_the_text_match()
    {
        var present = new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("ORG", false, Contains("Acme", negate: true))]);
        var absent = new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("ORG", false, Contains("Other", negate: true))]);
        Assert.False(DavFilterEvaluator.Matches(Vcard, present)); // value contains Acme → negate → no match
        Assert.True(DavFilterEvaluator.Matches(Vcard, absent));
    }

    [Fact]
    public void Is_not_defined_matches_only_when_absent()
    {
        Assert.True(DavFilterEvaluator.Matches(Vcard, new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("NICKNAME", true, null)])));
        Assert.False(DavFilterEvaluator.Matches(Vcard, new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("FN", true, null)])));
    }

    [Fact]
    public void Allof_requires_all_anyof_requires_one()
    {
        var props = new[] { new DavPropFilter("FN", false, Contains("jane")), new DavPropFilter("ORG", false, Contains("nope")) };
        Assert.False(DavFilterEvaluator.Matches(Vcard, new ContactQueryFilter(FilterTest.AllOf, props)));
        Assert.True(DavFilterEvaluator.Matches(Vcard, new ContactQueryFilter(FilterTest.AnyOf, props)));
    }

    [Fact]
    public void Param_filter_matches_on_a_property_parameter()
    {
        var needsAction = new DavPropFilter("ATTENDEE", false, null,
            [new DavParamFilter("PARTSTAT", false, new DavTextMatch("NEEDS-ACTION", TextMatchType.Contains, false))]);
        var accepted = new DavPropFilter("ATTENDEE", false, null,
            [new DavParamFilter("PARTSTAT", false, new DavTextMatch("ACCEPTED", TextMatchType.Contains, false))]);

        Assert.True(DavFilterEvaluator.Matches(Event, new CalendarQueryFilter("VEVENT", null, null, [needsAction])));
        Assert.False(DavFilterEvaluator.Matches(Event, new CalendarQueryFilter("VEVENT", null, null, [accepted])));
    }

    [Fact]
    public void Param_filter_present_and_is_not_defined()
    {
        var cnPresent = new DavPropFilter("ATTENDEE", false, null, [new DavParamFilter("CN", false, null)]);
        var noRsvp = new DavPropFilter("ATTENDEE", false, null, [new DavParamFilter("RSVP", true, null)]);
        Assert.True(DavFilterEvaluator.Matches(Event, new CalendarQueryFilter("VEVENT", null, null, [cnPresent])));
        Assert.True(DavFilterEvaluator.Matches(Event, new CalendarQueryFilter("VEVENT", null, null, [noRsvp])));
    }

    [Fact]
    public void Empty_filter_matches_everything()
    {
        Assert.True(DavFilterEvaluator.Matches(Vcard, new ContactQueryFilter(FilterTest.AllOf, [])));
    }

    [Fact]
    public void Grouped_property_matches_by_its_base_name()
    {
        // A vCard "group.PROPERTY" prefix (item1.EMAIL) must match a filter on EMAIL.
        const string grouped =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Grouped\r\nitem1.EMAIL:grouped@example\r\nEND:VCARD\r\n";
        var filter = new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("EMAIL", false, Contains("grouped@example"))]);
        Assert.True(DavFilterEvaluator.Matches(grouped, filter));
    }

    [Fact]
    public void Param_filter_matches_a_value_in_a_comma_list()
    {
        const string blob =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:e\r\nATTENDEE;MEMBER=a,b,c:mailto:x@t\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var match = new DavPropFilter("ATTENDEE", false, null, [new DavParamFilter("MEMBER", false, new DavTextMatch("b", TextMatchType.Equals, false))]);
        var miss = new DavPropFilter("ATTENDEE", false, null, [new DavParamFilter("MEMBER", false, new DavTextMatch("z", TextMatchType.Equals, false))]);
        Assert.True(DavFilterEvaluator.Matches(blob, new CalendarQueryFilter("VEVENT", null, null, [match])));
        Assert.False(DavFilterEvaluator.Matches(blob, new CalendarQueryFilter("VEVENT", null, null, [miss])));
    }

    [Fact]
    public void Param_filter_handles_a_quoted_value_containing_a_semicolon()
    {
        // The quoted CN contains ';' — it must not be treated as a parameter separator, and quotes are stripped.
        const string blob =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:e\r\n" +
            "ATTENDEE;CN=\"Doe; Jane\";PARTSTAT=ACCEPTED:mailto:jane@t\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var cn = new DavPropFilter("ATTENDEE", false, null, [new DavParamFilter("CN", false, Contains("Doe; Jane"))]);
        var partstat = new DavPropFilter("ATTENDEE", false, null, [new DavParamFilter("PARTSTAT", false, new DavTextMatch("ACCEPTED", TextMatchType.Equals, false))]);
        Assert.True(DavFilterEvaluator.Matches(blob, new CalendarQueryFilter("VEVENT", null, null, [cn])));
        Assert.True(DavFilterEvaluator.Matches(blob, new CalendarQueryFilter("VEVENT", null, null, [partstat])));
    }

    [Fact]
    public void Prop_value_and_param_must_both_match_in_the_param_path()
    {
        // ATTENDEE value contains "bob" AND has CN → matches; negating the value match → no match.
        var valueAndParam = new DavPropFilter("ATTENDEE", false, Contains("bob"), [new DavParamFilter("CN", false, null)]);
        var negatedValue = new DavPropFilter("ATTENDEE", false, Contains("bob", negate: true), [new DavParamFilter("CN", false, null)]);
        Assert.True(DavFilterEvaluator.Matches(Event, new CalendarQueryFilter("VEVENT", null, null, [valueAndParam])));
        Assert.False(DavFilterEvaluator.Matches(Event, new CalendarQueryFilter("VEVENT", null, null, [negatedValue])));
    }

    [Fact]
    public void All_param_filters_on_a_prop_must_match()
    {
        var bothMustMatch = new DavPropFilter("ATTENDEE", false, null,
        [
            new DavParamFilter("PARTSTAT", false, Contains("NEEDS-ACTION")),
            new DavParamFilter("CN", false, Contains("Zoe")), // no such CN
        ]);
        Assert.False(DavFilterEvaluator.Matches(Event, new CalendarQueryFilter("VEVENT", null, null, [bothMustMatch])));
    }

    [Fact]
    public void Param_filter_matches_when_any_occurrence_satisfies_it()
    {
        const string twoAttendees =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:e\r\n" +
            "ATTENDEE;PARTSTAT=ACCEPTED:mailto:a@t\r\nATTENDEE;PARTSTAT=NEEDS-ACTION:mailto:b@t\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var pending = new DavPropFilter("ATTENDEE", false, null, [new DavParamFilter("PARTSTAT", false, Contains("NEEDS-ACTION"))]);
        Assert.True(DavFilterEvaluator.Matches(twoAttendees, new CalendarQueryFilter("VEVENT", null, null, [pending])));
    }

    [Fact]
    public void Param_filter_negate_and_comma_list_trimming()
    {
        const string blob =
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:e\r\n" +
            "ATTENDEE;PARTSTAT=NEEDS-ACTION;MEMBER=\"a\", \"b\":mailto:x@t\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        // Negated param: PARTSTAT is not ACCEPTED → matches.
        var notAccepted = new DavPropFilter("ATTENDEE", false, null,
            [new DavParamFilter("PARTSTAT", false, new DavTextMatch("ACCEPTED", TextMatchType.Equals, Negate: true))]);
        // A comma-list value with surrounding spaces must be trimmed before an Equals match.
        var member = new DavPropFilter("ATTENDEE", false, null,
            [new DavParamFilter("MEMBER", false, new DavTextMatch("b", TextMatchType.Equals, false))]);
        Assert.True(DavFilterEvaluator.Matches(blob, new CalendarQueryFilter("VEVENT", null, null, [notAccepted])));
        Assert.True(DavFilterEvaluator.Matches(blob, new CalendarQueryFilter("VEVENT", null, null, [member])));
    }

    [Fact]
    public void Folded_value_is_unfolded_before_matching()
    {
        // RFC 5545/6350 line folding: a continuation line (leading space) is part of the value.
        const string folded =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:First\r\n NOTE-continued\r\nEND:VCARD\r\n";
        var filter = new ContactQueryFilter(FilterTest.AllOf, [new DavPropFilter("FN", false, Contains("FirstNOTE-continued"))]);
        Assert.True(DavFilterEvaluator.Matches(folded, filter));
    }
}
