using SimplCalCon.Client;

namespace SimplCalCon.WebTests;

public sealed class CollectionColorsTests
{
    [Fact]
    public void Stored_colour_is_used_verbatim()
    {
        Assert.Equal("#123456", CollectionColors.For(Guid.NewGuid(), "#123456"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_colour_falls_back_to_a_palette_hue(string? stored)
    {
        var color = CollectionColors.For(Guid.NewGuid(), stored);

        Assert.Matches("^#[0-9A-Fa-f]{6}$", color);
    }

    [Fact]
    public void Fallback_is_stable_for_the_same_id()
    {
        var id = Guid.NewGuid();

        Assert.Equal(CollectionColors.For(id, null), CollectionColors.For(id, null));
    }
}
