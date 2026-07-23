namespace SimplCalCon.Api.Http;

internal static class ResourceNames
{
    /// <summary>A URL-safe, unique resource name derived from a display name.</summary>
    public static string Slug(string name)
    {
        var normalized = new string((name ?? string.Empty).ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        var slug = string.Join('-', normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length == 0)
        {
            slug = "collection";
        }

        return $"{slug}-{Guid.NewGuid():N}";
    }
}
