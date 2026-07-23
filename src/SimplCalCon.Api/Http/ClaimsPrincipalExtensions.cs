using System.Security.Claims;
using OpenIddict.Abstractions;

namespace SimplCalCon.Api.Http;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user's id, read from the token subject (or NameIdentifier).</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(OpenIddictConstants.Claims.Subject)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("The authenticated principal has no usable subject claim.");
    }

    /// <summary>The authenticated user's tenant, or null for a platform administrator.</summary>
    public static Guid? GetTenantId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenantId) ? tenantId : null;
}
