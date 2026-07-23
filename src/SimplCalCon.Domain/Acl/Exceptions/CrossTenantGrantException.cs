namespace SimplCalCon.Domain.Acl.Exceptions;

/// <summary>
/// A grant was attempted between a collection and a principal in different tenants.
/// Cross-tenant sharing is not allowed in v1 (ADR 0006).
/// </summary>
public sealed class CrossTenantGrantException()
    : Exception("A collection can only be shared with principals in the same tenant.");
