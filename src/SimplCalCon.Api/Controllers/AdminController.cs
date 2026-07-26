using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Email;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Minimal administration reads (ADR 0034, 0006), role-gated in code: platform admins list
/// tenants; tenant admins list the users in their own tenant. A starting point — management
/// actions come later.
/// </summary>
[Route("api/admin")]
public sealed class AdminController(
    SimplCalConDbContext dbContext, ITenantEmailSettingsService emailSettings, IEmailSender emailSender,
    IGroupService groups, IAclService acl)
    : ApiControllerBase(acl)
{
    // --- Groups (ADR 0059): tenant-admin-managed, so group-based ACL grants are usable. ---

    [HttpGet("groups")]
    [HttpHead("groups")]
    public async Task<ActionResult<CollectionResource<GroupResource>>> Groups(CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        var list = await groups.ListAsync(tenantId, cancellationToken);
        return new CollectionResource<GroupResource>
        {
            Items = list.Select(g => new GroupResource(g.Id, g.Name, g.MemberCount)).ToList(),
            Links = { new Link("self", "/api/admin/groups") },
        };
    }

    [HttpPost("groups")]
    public async Task<ActionResult<GroupResource>> CreateGroup(
        [FromBody] CreateGroupRequest request, CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("A group name is required.");
        }

        var created = await groups.CreateAsync(tenantId, request.Name, cancellationToken);
        return created is null
            ? Conflict("A group with that name already exists.")
            : new GroupResource(created.Id, created.Name, created.MemberCount);
    }

    [HttpDelete("groups/{groupId:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid groupId, CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        return await groups.DeleteAsync(tenantId, groupId, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpGet("groups/{groupId:guid}/members")]
    [HttpHead("groups/{groupId:guid}/members")]
    public async Task<ActionResult<CollectionResource<GroupMemberResource>>> GroupMembers(
        Guid groupId, CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        var members = await groups.ListMembersAsync(tenantId, groupId, cancellationToken);
        return new CollectionResource<GroupMemberResource>
        {
            Items = members.Select(m => new GroupMemberResource(m.Id, m.Kind, m.DisplayName, m.Email)).ToList(),
            Links = { new Link("self", $"/api/admin/groups/{groupId}/members") },
        };
    }

    [HttpPut("groups/{groupId:guid}/members/{principalId:guid}")]
    public async Task<IActionResult> AddGroupMember(Guid groupId, Guid principalId, CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        return await groups.AddMemberAsync(tenantId, groupId, principalId, cancellationToken) switch
        {
            AddMemberResult.Added => NoContent(),
            AddMemberResult.WouldCycle => Conflict("That would create a group membership cycle."),
            _ => NotFound(),
        };
    }

    [HttpDelete("groups/{groupId:guid}/members/{principalId:guid}")]
    public async Task<IActionResult> RemoveGroupMember(Guid groupId, Guid principalId, CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        await groups.RemoveMemberAsync(tenantId, groupId, principalId, cancellationToken);
        return NoContent();
    }

    [HttpPost("email-settings/test")]
    public async Task<IActionResult> TestEmail(
        [FromBody] TestEmailRequest request, CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(request.To))
        {
            return BadRequest("A recipient address is required.");
        }

        var config = await emailSettings.GetConfigAsync(tenantId, cancellationToken);
        if (config is null || string.IsNullOrWhiteSpace(config.Host))
        {
            return BadRequest("Save the SMTP host and From address first.");
        }

        try
        {
            await emailSender.SendAsync(
                config, request.To, "SimplCalCon test email",
                "This is a test message from SimplCalCon — your SMTP settings are working.", cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            // Diagnostic endpoint: surface the SMTP failure (connection/auth/TLS) to the admin.
            return BadRequest($"Send failed: {ex.Message}");
        }
    }

    // --- Tenant SMTP / iMIP email settings (ADR 0047). Tenant-admin, own tenant. ---

    [HttpGet("email-settings")]
    [HttpHead("email-settings")]
    public async Task<ActionResult<TenantEmailSettingsResource>> EmailSettings(CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        var settings = await emailSettings.GetAsync(tenantId, cancellationToken);
        return settings is null
            ? new TenantEmailSettingsResource(false, string.Empty, 587, true, null, false, string.Empty, null,
                false, null, 993, true, null, false, "INBOX")
            : new TenantEmailSettingsResource(
                settings.Enabled, settings.Host, settings.Port, settings.UseStartTls, settings.Username,
                settings.HasPassword, settings.FromAddress, settings.FromName,
                settings.InboundEnabled, settings.ImapHost, settings.ImapPort, settings.ImapUseSsl,
                settings.ImapUsername, settings.HasImapPassword, settings.ImapFolder ?? "INBOX");
    }

    [HttpPut("email-settings")]
    public async Task<IActionResult> SaveEmailSettings(
        [FromBody] TenantEmailSettingsWriteRequest request, CancellationToken cancellationToken)
    {
        var tenantId = await RequireTenantAdminAsync(cancellationToken);
        if (request.Enabled && (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.FromAddress)))
        {
            return BadRequest("A host and From address are required to enable email.");
        }

        await emailSettings.SaveAsync(tenantId, new TenantEmailSettingsInput(
            request.Enabled, request.Host, request.Port, request.UseStartTls, request.Username,
            request.NewPassword, request.FromAddress, request.FromName,
            request.InboundEnabled, request.ImapHost, request.ImapPort, request.ImapUseSsl,
            request.ImapUsername, request.NewImapPassword, request.ImapFolder), cancellationToken);
        return NoContent();
    }

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

        // Which of these users have a profile photo (a computed exists-check — ADR 0035; no schema change).
        var userIds = users.Select(u => u.Id).ToList();
        var withPhoto = (await dbContext.UserProfilePhotos
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken)).ToHashSet();

        return new CollectionResource<AdminUserResource>
        {
            Items = users
                .Select(u => new AdminUserResource(
                    u.Id, u.DisplayName, u.Email, u.TenantRole?.ToString().ToLowerInvariant() ?? "member",
                    u.Status.ToString(), withPhoto.Contains(u.Id)))
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
