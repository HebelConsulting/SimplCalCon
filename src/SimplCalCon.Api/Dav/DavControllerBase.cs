using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Authentication;
using SimplCalCon.Api.Http;

namespace SimplCalCon.Api.Dav;

/// <summary>
/// Shared plumbing for the DAV controllers: app-password auth, owner enforcement
/// (no ACL yet — a user may only touch their own home), the Depth header, and href
/// construction. Principal-scoped layout (ADR 0003); no tenant in the path (ADR 0006).
/// </summary>
[Authorize(AuthenticationSchemes = DavAuthenticationDefaults.Scheme)]
public abstract class DavControllerBase : ControllerBase
{
    protected Guid CurrentUserId => User.GetUserId();

    protected Guid? CurrentTenantId => User.GetTenantId();

    /// <summary>403 unless the route principal is the authenticated user.</summary>
    protected IActionResult? RequireOwner(Guid routeUserId) =>
        routeUserId == CurrentUserId ? null : Forbid(DavAuthenticationDefaults.Scheme);

    protected int Depth()
    {
        var header = Request.Headers["Depth"].ToString();
        return header switch
        {
            "1" => 1,
            "infinity" => 1, // treated as one level; we never expand infinitely
            _ => 0,
        };
    }

    protected static string PrincipalHref(Guid userId) => $"/dav/principals/{userId}/";

    protected static string HomeHref(Guid userId) => $"/dav/addressbooks/{userId}/";

    protected static string CollectionHref(Guid userId, string book) => $"/dav/addressbooks/{userId}/{book}/";

    protected static string ObjectHref(Guid userId, string book, string name) =>
        $"/dav/addressbooks/{userId}/{book}/{name}";
}
