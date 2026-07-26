using Bunit;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using SimplCalCon.Client.Pages;

namespace SimplCalCon.WebTests;

/// <summary>
/// Guards the type-the-name safeguard on the irreversible "delete entire calendar / address book"
/// action: the confirm button stays disabled until the collection's exact name is typed (trimmed,
/// case-sensitive). The Delete trigger lives in the ribbon <c>SectionContent</c>, so each page is
/// hosted under a matching <see cref="SectionOutlet"/> to make the ribbon render.
/// </summary>
public sealed class CollectionDeleteGuardTests : TestContext
{
    private static readonly Guid Work = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static void HostWithRibbon<TPage>(RenderTreeBuilder b) where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        b.OpenComponent<SectionOutlet>(0);
        b.AddComponentParameter(1, nameof(SectionOutlet.SectionName), "RibbonSection");
        b.CloseComponent();
        b.OpenComponent<TPage>(2);
        b.CloseComponent();
    }

    private IRenderedFragment RenderCalendar()
    {
        this.UseFakeApi(new Dictionary<string, string>
        {
            ["/api/calendars"] = ApiHarness.List(
                new { id = Work, name = "Work", color = "#ff0000", supportsEvents = true, supportsTasks = true, shared = false }),
            [$"/api/calendars/{Work}/events"] = ApiHarness.List(),
        });
        return Render(HostWithRibbon<CalendarView>);
    }

    private IRenderedFragment RenderContacts()
    {
        this.UseFakeApi(new Dictionary<string, string>
        {
            ["/api/address-books"] = ApiHarness.List(
                new { id = Work, name = "Work", color = "#ff0000", shared = false }),
            [$"/api/address-books/{Work}/contacts"] = ApiHarness.List(),
        });
        return Render(HostWithRibbon<Contacts>);
    }

    private static void OpenDeleteModal(IRenderedFragment cut) =>
        cut.FindAll("button").First(x => x.TextContent.Trim() == "Delete").Click();

    // The confirm (class "danger") Delete button inside the open modal.
    private static AngleSharp.Dom.IElement ConfirmButton(IRenderedFragment cut) =>
        cut.FindAll(".modal-body button.danger").Single();

    [Theory]
    [InlineData("calendar")]
    [InlineData("contacts")]
    public void Confirm_button_disabled_until_exact_name_typed(string page)
    {
        var cut = page == "calendar" ? RenderCalendar() : RenderContacts();
        OpenDeleteModal(cut);

        // Nothing typed → the destructive action is blocked.
        Assert.True(ConfirmButton(cut).HasAttribute("disabled"));

        // A near miss (wrong case) stays blocked — the match is case-sensitive.
        cut.Find(".confirm-input").Input("work");
        cut.WaitForAssertion(() => Assert.True(ConfirmButton(cut).HasAttribute("disabled")));

        // The exact name (with incidental surrounding whitespace) unlocks it.
        cut.Find(".confirm-input").Input("  Work  ");
        cut.WaitForAssertion(() => Assert.False(ConfirmButton(cut).HasAttribute("disabled")));
    }
}
