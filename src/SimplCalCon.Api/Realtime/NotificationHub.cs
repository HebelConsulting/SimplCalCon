using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OpenIddict.Validation.AspNetCore;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Realtime;

/// <summary>
/// The live-update hub (ADR 0049). A connection auto-joins its owner's user group on connect
/// (for invitation-badge pushes); the client calls <see cref="Subscribe"/> for each collection
/// it is viewing so it receives that collection's change pings. Group membership is per-connection
/// and cleaned up automatically on disconnect. Authenticated with the same bearer scheme as the
/// REST surface (the token arrives in the WebSocket query string, lifted into the Authorization
/// header by <c>Program.cs</c>).
/// </summary>
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class NotificationHub(IAclService acl) : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public static string CollectionGroup(Guid collectionId) => $"collection:{collectionId}";

    public static string TenantAdminGroup(Guid tenantId) => $"admin:tenant:{tenantId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(user.GetUserId()));

        // Tenant admins also join their tenant's admin group for admin-list live refresh (ADR 0065).
        if (user.IsInRole("admin") && user.GetTenantId() is { } tenantId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantAdminGroup(tenantId));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Join a collection's change group after verifying the caller may read it.</summary>
    public async Task Subscribe(Guid collectionId)
    {
        var rights = await acl.GetEffectiveRightsAsync(Context.User!.GetUserId(), collectionId, Context.ConnectionAborted);
        if ((rights & AclRight.Read) == AclRight.Read)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, CollectionGroup(collectionId));
        }
    }

    /// <summary>Leave a collection's change group (when the client navigates away).</summary>
    public Task Unsubscribe(Guid collectionId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CollectionGroup(collectionId));
}
