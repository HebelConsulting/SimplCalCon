namespace SimplCalCon.Domain.Acl;

/// <summary>
/// Fine-grained, combinable rights a grant confers on a collection (ADR 0007). The
/// owner implicitly holds all of them. Stored as a flags value.
/// </summary>
[Flags]
public enum AclRight
{
    None = 0,
    Read = 1,
    WriteContent = 2,
    Create = 4,
    Delete = 8,
    Share = 16,
    Admin = 32,
}
