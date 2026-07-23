namespace SimplCalCon.Domain.Objects;

/// <summary>A contact (one vCard). Extracted fields back search/autocomplete.</summary>
public class ContactObject : CollectionObject
{
    /// <summary>Formatted display name (vCard FN).</summary>
    public string? FormattedName { get; set; }

    public string? FamilyName { get; set; }

    public string? GivenName { get; set; }

    public string? Organization { get; set; }

    /// <summary>Lowercased, semicolon-joined email addresses for substring search.</summary>
    public string? Emails { get; set; }

    /// <summary>Semicolon-joined phone numbers for substring search.</summary>
    public string? Phones { get; set; }
}
