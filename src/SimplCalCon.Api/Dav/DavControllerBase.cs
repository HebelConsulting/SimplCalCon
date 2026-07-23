using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Authentication;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Collections;

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

    /// <summary>403 unless the route principal is the authenticated user (owner-only operations).</summary>
    protected IActionResult? RequireOwner(Guid routeUserId) =>
        routeUserId == CurrentUserId ? null : ForbidDav();

    protected IActionResult ForbidDav() => Forbid(DavAuthenticationDefaults.Scheme);

    protected const AclRight AllRights =
        AclRight.Read | AclRight.WriteContent | AclRight.Create | AclRight.Delete | AclRight.Share | AclRight.Admin;

    /// <summary>
    /// True when the caller may perform an operation needing <paramref name="required"/> on
    /// the collection: they own it, or their effective rights (direct + group grants) include it (ADR 0007).
    /// </summary>
    protected async Task<bool> HasAccessAsync(
        Collection collection, AclRight required, IAclService acl, CancellationToken cancellationToken)
    {
        if (collection.OwnerId == CurrentUserId)
        {
            return true;
        }

        var rights = await acl.GetEffectiveRightsAsync(CurrentUserId, collection.Id, cancellationToken);
        return (rights & required) == required;
    }

    /// <summary>The caller's effective rights on the collection (owner ⇒ all), for privilege reporting (ADR 0023).</summary>
    protected async Task<AclRight> EffectiveRightsAsync(
        Collection collection, IAclService acl, CancellationToken cancellationToken) =>
        collection.OwnerId == CurrentUserId
            ? AllRights
            : await acl.GetEffectiveRightsAsync(CurrentUserId, collection.Id, cancellationToken);

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

    protected static string CalendarHomeHref(Guid userId) => $"/dav/calendars/{userId}/";

    protected static string CalendarHref(Guid userId, string calendar) => $"/dav/calendars/{userId}/{calendar}/";

    protected static string CalendarObjectHref(Guid userId, string calendar, string name) =>
        $"/dav/calendars/{userId}/{calendar}/{name}";
}
