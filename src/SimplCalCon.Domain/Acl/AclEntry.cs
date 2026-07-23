using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Common;
using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Domain.Acl;

/// <summary>
/// A grant of <see cref="AclRight"/>s on a <see cref="Collection"/> to a
/// <see cref="Principal"/> (a user or a group). At most one grant per principal per
/// collection; effective rights aggregate direct and transitive-group grants (ADR 0007).
/// </summary>
public class AclEntry : IHasConcurrencyToken
{
    public Guid Id { get; set; }

    public Guid CollectionId { get; set; }

    public Collection? Collection { get; set; }

    /// <summary>The grantee — a user or a group.</summary>
    public Guid PrincipalId { get; set; }

    public Principal? Principal { get; set; }

    public AclRight Rights { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid ConcurrencyToken { get; set; }
}
