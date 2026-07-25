using Bunit;
using SimplCalCon.Client.Layout;

namespace SimplCalCon.WebTests;

/// <summary>Component guards for the reusable collections pane (ADR 0062/0063).</summary>
public sealed class CollectionsPaneTests : TestContext
{
    private static readonly Guid Work = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Personal = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static List<CollectionsPane.Item> TwoBooks() =>
    [
        new(Work, "Work", "#ff0000", Shared: false),
        new(Personal, "Personal", null, Shared: true),
    ];

    [Fact]
    public void Renders_a_row_per_collection_with_its_name()
    {
        var cut = RenderComponent<CollectionsPane>(p => p
            .Add(x => x.Collections, TwoBooks())
            .Add(x => x.Checked, new HashSet<Guid> { Work, Personal }));

        var names = cut.FindAll(".coll-name-text").Select(n => n.TextContent).ToList();
        Assert.Equal(["Work", "Personal"], names);
    }

    [Fact]
    public void Shared_collection_has_no_colour_picker_but_an_owned_one_does()
    {
        var cut = RenderComponent<CollectionsPane>(p => p
            .Add(x => x.Collections, TwoBooks())
            .Add(x => x.Checked, new HashSet<Guid> { Work, Personal }));

        var rows = cut.FindAll(".coll-row");
        Assert.Single(rows[0].QuerySelectorAll("input[type=color]"));  // Work (owned)
        Assert.Empty(rows[1].QuerySelectorAll("input[type=color]"));   // Personal (shared)
    }

    [Fact]
    public void Active_collection_row_is_highlighted()
    {
        var cut = RenderComponent<CollectionsPane>(p => p
            .Add(x => x.Collections, TwoBooks())
            .Add(x => x.Checked, new HashSet<Guid> { Work, Personal })
            .Add(x => x.ActiveId, Personal));

        var rows = cut.FindAll(".coll-row");
        Assert.DoesNotContain("active", rows[0].ClassList);
        Assert.Contains("active", rows[1].ClassList);
    }

    [Fact]
    public void Unchecking_a_box_mutates_the_set_and_raises_the_filter_event()
    {
        var checkedSet = new HashSet<Guid> { Work, Personal };
        var filterRaised = 0;
        var cut = RenderComponent<CollectionsPane>(p => p
            .Add(x => x.Collections, TwoBooks())
            .Add(x => x.Checked, checkedSet)
            .Add(x => x.OnFilterChanged, () => filterRaised++));

        cut.FindAll(".coll-check")[0].Change(false);

        Assert.DoesNotContain(Work, checkedSet);
        Assert.Equal(1, filterRaised);
    }

    [Fact]
    public void Clicking_a_name_raises_the_active_event_with_that_id()
    {
        Guid? activated = null;
        var cut = RenderComponent<CollectionsPane>(p => p
            .Add(x => x.Collections, TwoBooks())
            .Add(x => x.Checked, new HashSet<Guid> { Work, Personal })
            .Add(x => x.OnActiveChanged, id => activated = id));

        cut.FindAll(".coll-name")[1].Click();

        Assert.Equal(Personal, activated);
    }

    [Fact]
    public void Picking_a_colour_raises_the_colour_event_with_id_and_hex()
    {
        (Guid Id, string Color)? picked = null;
        var cut = RenderComponent<CollectionsPane>(p => p
            .Add(x => x.Collections, TwoBooks())
            .Add(x => x.Checked, new HashSet<Guid> { Work, Personal })
            .Add(x => x.OnColorChanged, change => picked = change));

        cut.Find(".coll-row .coll-swatch input[type=color]").Change("#00ff00");

        Assert.Equal((Work, "#00ff00"), picked);
    }
}
