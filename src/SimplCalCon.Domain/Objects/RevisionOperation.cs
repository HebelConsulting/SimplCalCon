namespace SimplCalCon.Domain.Objects;

/// <summary>The kind of change an <see cref="ObjectRevision"/> records.</summary>
public enum RevisionOperation
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
}
