using Bunit;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using SimplCalCon.Client.Pages;

namespace SimplCalCon.WebTests;

/// <summary>
/// Guards the mandatory pre-purge backup (ADR 0078): in the "delete permanently" modal, the destructive
/// button stays disabled until the user has BOTH downloaded a backup AND typed the collection's name.
/// The page is hosted under a <see cref="SectionOutlet"/> so its ribbon renders; the purge modal is
/// opened from the pane's 🗑 action on a deleted collection.
/// </summary>
public sealed class CollectionPurgeGuardTests : TestContext
{
    private static readonly Guid Live = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Gone = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private IRenderedFragment RenderCalendar()
    {
        this.UseFakeApi(new Dictionary<string, string>
        {
            // The pane only renders when there's at least one live calendar; the deleted one shows in its footer.
            ["/api/calendars"] = ApiHarness.List(
                new { id = Live, name = "Live", supportsEvents = true, supportsTasks = true, shared = false }),
            ["/api/calendars/deleted"] = ApiHarness.List(
                new { id = Gone, name = "BackupMe", supportsEvents = true, supportsTasks = true, shared = false }),
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

    private static AngleSharp.Dom.IElement PurgeButton(IRenderedFragment cut) =>
        cut.FindAll(".modal-body button.danger").Single();

    [Fact]
    public void Permanent_delete_requires_both_a_downloaded_backup_and_the_typed_name()
    {
        var cut = RenderCalendar();

        cut.Find(".pane-deleted-toggle").Click();   // expand the "Deleted (1)" section
        cut.Find(".coll-purge").Click();            // open the purge modal

        // Nothing done yet → blocked.
        Assert.True(PurgeButton(cut).HasAttribute("disabled"));

        // Name typed but no backup → still blocked (the new mandatory-export gate).
        cut.Find(".modal-body .confirm-input").Input("BackupMe");
        cut.WaitForAssertion(() => Assert.True(PurgeButton(cut).HasAttribute("disabled")));

        // Download the backup → now both conditions met → enabled.
        cut.FindAll(".modal-body button").First(b => b.TextContent.Contains("Download backup")).Click();
        cut.WaitForAssertion(() => Assert.False(PurgeButton(cut).HasAttribute("disabled")));
    }

    [Fact]
    public void A_downloaded_backup_alone_does_not_unlock_permanent_delete()
    {
        var cut = RenderCalendar();
        cut.Find(".pane-deleted-toggle").Click();
        cut.Find(".coll-purge").Click();

        // Backup downloaded but name not typed → still blocked.
        cut.FindAll(".modal-body button").First(b => b.TextContent.Contains("Download backup")).Click();
        cut.WaitForAssertion(() => Assert.True(PurgeButton(cut).HasAttribute("disabled")));
    }
}
