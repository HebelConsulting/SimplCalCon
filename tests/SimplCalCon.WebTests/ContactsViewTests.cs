using Bunit;
using SimplCalCon.Client.Pages;

namespace SimplCalCon.WebTests;

/// <summary>Render guards for the Contacts tab's merged, colour-coded multi-book view (ADR 0062/0063).</summary>
public sealed class ContactsViewTests : TestContext
{
    private static readonly Guid Friends = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Family = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private IRenderedComponent<Contacts> RenderContacts()
    {
        this.UseFakeApi(new Dictionary<string, string>
        {
            ["/api/address-books"] = ApiHarness.List(
                new { id = Friends, name = "Friends", color = "#00ff00", shared = false },
                new { id = Family, name = "Family", color = (string?)null, shared = false }),
            [$"/api/address-books/{Friends}/contacts"] = ApiHarness.List(
                new { id = Guid.NewGuid(), formattedName = "Alice", organization = "Acme", emails = new[] { "alice@x.test" }, phones = Array.Empty<string>(), hasPhoto = true }),
            [$"/api/address-books/{Family}/contacts"] = ApiHarness.List(
                new { id = Guid.NewGuid(), formattedName = "Bob", organization = (string?)null, emails = Array.Empty<string>(), phones = new[] { "+123" }, hasPhoto = false }),
        });

        return RenderComponent<Contacts>();
    }

    [Fact]
    public void Pane_lists_every_address_book()
    {
        var cut = RenderContacts();
        Assert.Equal(["Friends", "Family"], cut.FindAll(".coll-name-text").Select(n => n.TextContent));
    }

    [Fact]
    public void List_merges_contacts_from_all_checked_books_with_colour_and_book_columns()
    {
        var cut = RenderContacts();

        var rows = cut.FindAll(".contact-table tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Single(r.QuerySelectorAll(".color-col .swatch")));
        Assert.Contains("Alice", cut.Markup);
        Assert.Contains("Bob", cut.Markup);
        Assert.Contains("Friends", cut.Markup);
        Assert.Contains("Family", cut.Markup);
        Assert.Contains("background:#00ff00", cut.Markup); // Friends' explicit colour
    }

    [Fact]
    public void Unchecking_a_book_filters_its_contacts_out()
    {
        var cut = RenderContacts();
        Assert.Contains("Alice", cut.Markup);

        cut.FindAll(".coll-check")[0].Change(false); // hide Friends

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Alice", cut.Markup);
            Assert.Contains("Bob", cut.Markup);
        });
    }

    [Fact]
    public void Photo_filter_is_tri_state_any_with_without()
    {
        var cut = RenderContacts();   // Alice has a photo, Bob does not
        var select = cut.Find(".photo-filter");

        select.Change("With");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alice", cut.Markup);
            Assert.DoesNotContain("Bob", cut.Markup);
        });

        select.Change("Without");
        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Alice", cut.Markup);
            Assert.Contains("Bob", cut.Markup);
        });

        select.Change("Any");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alice", cut.Markup);
            Assert.Contains("Bob", cut.Markup);
        });
    }
}
