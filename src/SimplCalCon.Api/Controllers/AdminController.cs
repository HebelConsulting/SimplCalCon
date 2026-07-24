using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Minimal administration reads (ADR 0034, 0006), role-gated in code: platform admins list
/// tenants; tenant admins list the users in their own tenant. A starting point — management
/// actions come later.
/// </summary>
[Route("api/admin")]
public sealed class AdminController(SimplCalConDbContext dbContext, IAclService acl) : ApiControllerBase(acl)
{
    [HttpGet("tenants")]
    public async Task<ActionResult<CollectionResource<TenantResource>>> Tenants(CancellationToken cancellationToken)
    {
        await RequirePlatformAdminAsync(cancellationToken);

        var tenants = await dbContext.Tenants.OrderBy(t => t.Name).ToListAsync(cancellationToken);
        return new CollectionResource<TenantResource>
        {
            Items = tenants.Select(t => new TenantResource(t.Id, t.Name, t.Slug, t.Status.ToString())).ToList(),
            Links = { new Link("self", "/api/admin/tenants") },
        };
    }

    [HttpHead("tenants")]
    public async Task<IActionResult> HeadTenants(CancellationToken cancellationToken)
    {
        await RequirePlatformAdminAsync(cancellationToken);
        return Ok();
    }

    [HttpGet("users")]
    public async Task<ActionResult<CollectionResource<AdminUserResource>>> Users(CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);

        var users = await dbContext.Users
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken);

        return new CollectionResource<AdminUserResource>
        {
            Items = users
                .Select(u => new AdminUserResource(
                    u.Id, u.DisplayName, u.Email, u.TenantRole?.ToString().ToLowerInvariant() ?? "member", u.Status.ToString()))
                .ToList(),
            Links = { new Link("self", "/api/admin/users") },
        };
    }

    [HttpHead("users")]
    public async Task<IActionResult> HeadUsers(CancellationToken cancellationToken)
    {
        await RequireTenantAdminAsync(cancellationToken);
        return Ok();
    }

    private async Task RequirePlatformAdminAsync(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        if (user is not { IsPlatformAdministrator: true })
        {
            throw new InsufficientRightsException();
        }
    }

    private async Task<Guid> RequireTenantAdminAsync(CancellationToken cancellationToken)
    {
        var user = await CurrentUserAsync(cancellationToken);
        return user is { TenantId: { } tenantId, TenantRole: TenantRole.Admin }
            ? tenantId
            : throw new InsufficientRightsException();
    }

    private Task<User?> CurrentUserAsync(CancellationToken cancellationToken) =>
        dbContext.Users.FirstOrDefaultAsync(u => u.Id == CurrentUserId, cancellationToken);
}
