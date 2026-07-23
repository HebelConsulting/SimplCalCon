namespace SimplCalCon.Domain.Objects;

/// <summary>The kind of change an <see cref="ObjectRevision"/> records.</summary>
public enum RevisionOperation
{
    Created = 0,
    Updated = 1,
    Deleted = 2,

    /// <summary>The object was brought back from the trash, or a prior revision was reinstated (ADR 0028).</summary>
    Restored = 3,
}
