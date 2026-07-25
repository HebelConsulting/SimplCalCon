using Bunit;
using SimplCalCon.Client.Layout;

namespace SimplCalCon.WebTests;

/// <summary>Component guards for the reusable collections pane (ADR 0062/0063/0066).</summary>
public sealed class CollectionsPaneTests : TestContext
{
    private static readonly Guid Work = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Personal = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Work has a personal colour override (reset available); Personal is shared with no override.
    private static List<CollectionsPane.Item> TwoBooks() =>
    [
        new(Work, "Work", "#ff0000", HasOwnColor: true, Shared: false),
        new(Personal, "Personal", "#00ff00", HasOwnColor: false, Shared: true),
    ];

    private IRenderedComponent<CollectionsPane> Render(Action<ComponentParameterCollectionBuilder<CollectionsPane>>? extra = null) =>
        RenderComponent<CollectionsPane>(p =>
        {
            p.Add(x => x.Collections, TwoBooks());
            p.Add(x => x.Checked, new HashSet<Guid> { Work, Personal });
            extra?.Invoke(p);
        });

    [Fact]
    public void Renders_a_row_per_collection_with_its_name()
    {
        var cut = Render();
        Assert.Equal(["Work", "Personal"], cut.FindAll(".coll-name-text").Select(n => n.TextContent));
    }

    [Fact]
    public void Every_row_has_a_personal_colour_picker()
    {
        var rows = Render().FindAll(".coll-row");
        Assert.Single(rows[0].QuerySelectorAll("input[type=color]")); // owned
        Assert.Single(rows[1].QuerySelectorAll("input[type=color]")); // shared — everyone can set their own (ADR 0066)
    }

    [Fact]
    public void Reset_appears_only_when_a_personal_colour_is_set()
    {
        var rows = Render().FindAll(".coll-row");
        Assert.Single(rows[0].QuerySelectorAll(".coll-reset")); // Work has an override
        Assert.Empty(rows[1].QuerySelectorAll(".coll-reset"));  // Personal doesn't
    }

    [Fact]
    public void Active_collection_row_is_highlighted()
    {
        var cut = Render(p => p.Add(x => x.ActiveId, Personal));
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
        var cut = Render(p => p.Add(x => x.OnActiveChanged, id => activated = id));

        cut.FindAll(".coll-name")[1].Click();

        Assert.Equal(Personal, activated);
    }

    [Fact]
    public void Picking_a_colour_raises_the_colour_event_with_id_and_hex()
    {
        (Guid Id, string Color)? picked = null;
        var cut = Render(p => p.Add(x => x.OnColorChanged, change => picked = change));

        cut.Find(".coll-row .coll-swatch input[type=color]").Change("#0000ff");

        Assert.Equal((Work, "#0000ff"), picked);
    }

    [Fact]
    public void Clicking_reset_raises_the_reset_event_with_the_id()
    {
        Guid? reset = null;
        var cut = Render(p => p.Add(x => x.OnColorReset, id => reset = id));

        cut.FindAll(".coll-reset")[0].Click(); // only Work has a reset

        Assert.Equal(Work, reset);
    }
}
