using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Domain.Authentication;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Configuration;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Security;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SimplCalCon.Infrastructure.Bootstrap;

/// <summary>
/// First-run initialization (ADR 0016): applies migrations, registers the SPA OIDC
/// client, seeds the platform administrator if none exists, and — in Development —
/// an optional demo tenant + admin so tenant-scoped sign-in is testable.
/// </summary>
internal sealed class BootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IClock clock,
    IOptions<BootstrapOptions> bootstrapOptions,
    IOptions<SpaClientOptions> spaOptions,
    ILogger<BootstrapHostedService> logger) : IHostedService
{
    private readonly BootstrapOptions _bootstrap = bootstrapOptions.Value;
    private readonly SpaClientOptions _spa = spaOptions.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<SimplCalConDbContext>();
        var passwordHashing = services.GetRequiredService<PasswordHashing>();

        await dbContext.Database.MigrateAsync(cancellationToken);
        await SeedSpaClientAsync(services, cancellationToken);
        await SeedPlatformAdminAsync(services, dbContext, passwordHashing, cancellationToken);
        await SeedDemoTenantAsync(dbContext, passwordHashing, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedSpaClientAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
        if (await manager.FindByClientIdAsync(SpaClientOptions.ClientId, cancellationToken) is not null)
        {
            return;
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = SpaClientOptions.ClientId,
            ClientType = ClientTypes.Public,
            ApplicationType = ApplicationTypes.Web,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "SimplCalCon Web",
            RedirectUris = { new Uri($"{_spa.BaseUrl.TrimEnd('/')}{_spa.LoginCallbackPath}") },
            PostLogoutRedirectUris = { new Uri($"{_spa.BaseUrl.TrimEnd('/')}{_spa.LogoutCallbackPath}") },
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
                Permissions.Prefixes.Scope + AuthScopes.Api,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        await manager.CreateAsync(descriptor, cancellationToken);
        logger.LogInformation("Registered OIDC client '{ClientId}'.", SpaClientOptions.ClientId);
    }

    private async Task SeedPlatformAdminAsync(
        IServiceProvider services,
        SimplCalConDbContext dbContext,
        PasswordHashing passwordHashing,
        CancellationToken cancellationToken)
    {
        if (_bootstrap.PlatformAdmin is not { } seed || string.IsNullOrWhiteSpace(seed.Email))
        {
            return;
        }

        if (await dbContext.Users.AnyAsync(u => u.TenantId == null, cancellationToken))
        {
            return;
        }

        var admin = new User
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            DisplayName = seed.DisplayName,
            Email = seed.Email,
            NormalizedEmail = seed.Email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = clock.UtcNow,
        };

        if (!string.IsNullOrEmpty(seed.Password))
        {
            admin.PasswordHash = passwordHashing.Hash(seed.Password);
            admin.Status = UserStatus.Active;
            dbContext.Users.Add(admin);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded platform administrator '{Email}' (active).", admin.Email);
            return;
        }

        admin.Status = UserStatus.Invited;
        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);

        var activation = services.GetRequiredService<IAccountActivationService>();
        var issued = await activation.IssueAsync(admin.Id, TokenPurpose.Activation, admin.Id, cancellationToken);
        logger.LogWarning(
            "Seeded platform administrator '{Email}' (invited). Activation token (deliver out-of-band): {Token}",
            admin.Email, issued.RawToken);
    }

    private async Task SeedDemoTenantAsync(
        SimplCalConDbContext dbContext, PasswordHashing passwordHashing, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()
            || _bootstrap.DemoTenant is not { } seed
            || string.IsNullOrWhiteSpace(seed.AdminEmail))
        {
            return;
        }

        var slug = seed.TenantSlug.ToLowerInvariant();
        if (await dbContext.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken))
        {
            return;
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = seed.TenantName,
            Slug = slug,
            Status = TenantStatus.Active,
            CreatedAt = clock.UtcNow,
        };
        dbContext.Tenants.Add(tenant);

        var admin = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            DisplayName = seed.AdminDisplayName,
            Email = seed.AdminEmail,
            NormalizedEmail = seed.AdminEmail.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = clock.UtcNow,
            TenantRole = TenantRole.Admin,
            Status = UserStatus.Active,
            PasswordHash = string.IsNullOrEmpty(seed.AdminPassword)
                ? null
                : passwordHashing.Hash(seed.AdminPassword),
        };
        dbContext.Users.Add(admin);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded demo tenant '{Slug}' with admin '{Email}'.", slug, admin.Email);
    }
}

/// <summary>Custom OAuth scopes exposed by the API.</summary>
internal static class AuthScopes
{
    public const string Api = "simplcalcon.api";
}
