using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Api.Controllers;

/// <summary>The authenticated user's own profile (<c>GET /api/me</c>).</summary>
[ApiController]
[Route("api/me")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class MeController(SimplCalConDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResource>> Get(CancellationToken cancellationToken)
    {
        var user = await FindAsync(cancellationToken);
        return user is null ? NotFound() : Map(user);
    }

    [HttpHead]
    public async Task<IActionResult> Head(CancellationToken cancellationToken)
    {
        var user = await FindAsync(cancellationToken);
        return user is null ? NotFound() : Ok();
    }

    private Task<User?> FindAsync(CancellationToken cancellationToken)
    {
        var id = User.GetUserId();
        return dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    private static MeResource Map(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        TenantId = user.TenantId,
        Role = user.IsPlatformAdministrator ? "platform_admin" : user.TenantRole?.ToString().ToLowerInvariant() ?? "member",
        Links =
        {
            new Link("self", "/api/me"),
            new Link("app-passwords", "/api/app-passwords"),
        },
    };
}
