using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Errors.Exceptions.Users;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// User profile photos (ADR 0035). Clients send a normalized 256×256 PNG; the server only
/// byte-guards and stores it (no image library). Auth = self, or a tenant admin acting on a
/// user in their own tenant.
/// </summary>
[Route("api/users")]
public sealed class UsersController(SimplCalConDbContext dbContext, IClock clock, IAclService acl)
    : ApiControllerBase(acl)
{
    [HttpGet("me/photo")]
    [HttpHead("me/photo")]
    public Task<IActionResult> GetMyPhoto(CancellationToken cancellationToken) =>
        GetPhoto(CurrentUserId, cancellationToken);

    [HttpGet("{id:guid}/photo")]
    [HttpHead("{id:guid}/photo")]
    public async Task<IActionResult> GetPhoto(Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeTargetAsync(id, cancellationToken);

        if (HttpMethods.IsHead(Request.Method))
        {
            return await dbContext.UserProfilePhotos.AnyAsync(p => p.UserId == id, cancellationToken)
                ? Ok()
                : NotFound();
        }

        var photo = await dbContext.UserProfilePhotos.AsNoTracking()
            .Where(p => p.UserId == id)
            .Select(p => p.Photo)
            .FirstOrDefaultAsync(cancellationToken);

        return photo is null ? NotFound() : File(photo, "image/png");
    }

    [HttpPut("me/photo")]
    public Task<IActionResult> PutMyPhoto(CancellationToken cancellationToken) =>
        PutPhoto(CurrentUserId, cancellationToken);

    [HttpPut("{id:guid}/photo")]
    public async Task<IActionResult> PutPhoto(Guid id, CancellationToken cancellationToken)
    {
        var target = await AuthorizeTargetAsync(id, cancellationToken);

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (!ProfilePhotoValidator.IsValid(bytes))
        {
            throw new InvalidProfilePhotoException();
        }

        var existing = await dbContext.UserProfilePhotos.FirstOrDefaultAsync(p => p.UserId == id, cancellationToken);
        if (existing is null)
        {
            dbContext.UserProfilePhotos.Add(new UserProfilePhoto
            {
                UserId = id,
                TenantId = target.TenantId,
                Photo = bytes,
                UpdatedAt = clock.UtcNow.UtcDateTime,
            });
        }
        else
        {
            existing.Photo = bytes;
            existing.UpdatedAt = clock.UtcNow.UtcDateTime;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("me/photo")]
    public Task<IActionResult> DeleteMyPhoto(CancellationToken cancellationToken) =>
        DeletePhoto(CurrentUserId, cancellationToken);

    [HttpDelete("{id:guid}/photo")]
    public async Task<IActionResult> DeletePhoto(Guid id, CancellationToken cancellationToken)
    {
        await AuthorizeTargetAsync(id, cancellationToken);
        await dbContext.UserProfilePhotos.Where(p => p.UserId == id).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    private async Task<User> AuthorizeTargetAsync(Guid targetId, CancellationToken cancellationToken)
    {
        var target = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetId, cancellationToken)
            ?? throw new InsufficientRightsException();

        if (targetId == CurrentUserId)
        {
            return target;
        }

        var caller = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == CurrentUserId, cancellationToken);
        if (caller is { TenantRole: TenantRole.Admin, TenantId: { } tenantId } && target.TenantId == tenantId)
        {
            return target;
        }

        throw new InsufficientRightsException();
    }
}
