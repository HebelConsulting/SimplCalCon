using Bunit;
using SimplCalCon.Client.Layout;

namespace SimplCalCon.WebTests;

/// <summary>Guards the reusable read-only avatar (ADR 0035): initials when no photo, image when HasPhoto.</summary>
public sealed class AvatarTests : TestContext
{
    [Fact]
    public void Shows_initials_and_no_image_when_the_user_has_no_photo()
    {
        this.UseFakeApi(new Dictionary<string, string>());

        var cut = RenderComponent<Avatar>(p => p
            .Add(x => x.Initials, "JD")
            .Add(x => x.HasPhoto, false)
            .Add(x => x.Path, "api/users/1/photo"));

        Assert.Empty(cut.FindAll("img"));
        Assert.Contains("JD", cut.Markup);
    }

    [Fact]
    public void Fetches_and_shows_the_photo_when_the_user_has_one()
    {
        this.UseFakeApi(new Dictionary<string, string>()); // any GET returns 200 → a data URL is produced

        var cut = RenderComponent<Avatar>(p => p
            .Add(x => x.Initials, "JD")
            .Add(x => x.HasPhoto, true)
            .Add(x => x.Path, "api/users/1/photo"));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("img")));
    }
}
