using Bunit;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using SimplCalCon.Client.Pages;

namespace SimplCalCon.WebTests;

/// <summary>
/// Guards the fix for the "deleting your only collection strands it" gap (ADR 0078 follow-up): with
/// zero live collections but a deleted one, the pane still renders so its "Deleted" footer
/// (restore/purge) is reachable; with nothing at all, the "none yet" message shows instead.
/// </summary>
public sealed class DeletedOnlyPaneTests : TestContext
{
    private static readonly Guid Gone = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private IRenderedFragment RenderCalendar(bool withDeleted)
    {
        this.UseFakeApi(new Dictionary<string, string>
        {
            ["/api/calendars"] = ApiHarness.List(), // no live calendars
            ["/api/calendars/deleted"] = withDeleted
                ? ApiHarness.List(new { id = Gone, name = "BackupMe", supportsEvents = true, supportsTasks = true, shared = false })
                : ApiHarness.List(),
        });

        return Render(b =>
        {
            b.OpenComponent<SectionOutlet>(0);
            b.AddComponentParameter(1, nameof(SectionOutlet.SectionName), "RibbonSection");
            b.CloseComponent();
            b.OpenComponent<CalendarView>(2);
            b.CloseComponent();
        });
    }

    [Fact]
    public void Pane_and_deleted_footer_render_when_only_deleted_collections_exist()
    {
        var cut = RenderCalendar(withDeleted: true);

        Assert.NotEmpty(cut.FindAll(".collections-pane"));
        Assert.NotEmpty(cut.FindAll(".pane-deleted-toggle"));   // the Deleted footer is reachable
        Assert.DoesNotContain("No calendars yet", cut.Markup);
    }

    [Fact]
    public void Shows_the_empty_message_when_there_are_no_collections_at_all()
    {
        var cut = RenderCalendar(withDeleted: false);

        Assert.Contains("No calendars yet", cut.Markup);
        Assert.Empty(cut.FindAll(".collections-pane"));
    }
}
