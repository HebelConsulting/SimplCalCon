namespace SimplCalCon.Client;

/// <summary>
/// Resolves the display colour for a collection (ADR 0062): the owner-set colour if present, else a
/// stable colour picked from a palette by the collection id — so an uncoloured collection still gets
/// a consistent hue across the pane, the list swatch, and the grid chips.
/// </summary>
public static class CollectionColors
{
    private static readonly string[] Palette =
    [
        "#3B82F6", "#EF4444", "#10B981", "#F59E0B", "#8B5CF6",
        "#EC4899", "#14B8A6", "#F97316", "#6366F1", "#84CC16",
    ];

    public static string For(Guid id, string? stored) =>
        !string.IsNullOrWhiteSpace(stored) ? stored! : Palette[(int)((uint)id.GetHashCode() % Palette.Length)];

    /// <summary>The effective colour for a user (ADR 0066): personal override, else owner default, else palette.</summary>
    public static string Effective(Guid id, string? myColor, string? ownerColor) => For(id, myColor ?? ownerColor);
}
