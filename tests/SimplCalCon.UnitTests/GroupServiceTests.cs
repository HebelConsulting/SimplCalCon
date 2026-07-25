using Microsoft.Extensions.Logging.Abstractions;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Identity;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

/// <summary>Group management fires the admin-list live-refresh signal (ADR 0065).</summary>
public sealed class GroupServiceTests
{
    private readonly TestDatabase _database = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly CapturingNotifier _notifier = new();

    private GroupService Service(SimplCalConDbContext context) =>
        new(context, _clock, new EmptyDirectory(), _notifier, NullLogger<GroupService>.Instance);

    [Fact]
    public async Task Creating_a_group_notifies_the_tenant_admins()
    {
        var tenantId = await SeedTenantAsync();
        await using var context = _database.CreateContext();

        await Service(context).CreateAsync(tenantId, "Marketing", default);

        Assert.Equal(tenantId, _notifier.LastAdminTenant);
    }

    [Fact]
    public async Task Adding_a_member_notifies_the_tenant_admins()
    {
        var tenantId = await SeedTenantAsync();
        Guid groupId, userId;
        await using (var context = _database.CreateContext())
        {
            groupId = (await Service(context).CreateAsync(tenantId, "Team", default))!.Id;
            userId = await SeedUserAsync(context, tenantId);
        }

        _notifier.LastAdminTenant = null;
        await using (var context = _database.CreateContext())
        {
            var result = await Service(context).AddMemberAsync(tenantId, groupId, userId, default);
            Assert.Equal(AddMemberResult.Added, result);
        }

        Assert.Equal(tenantId, _notifier.LastAdminTenant);
    }

    private async Task<Guid> SeedTenantAsync()
    {
        await using var context = _database.CreateContext();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "T", Slug = $"t-{Guid.NewGuid():N}", CreatedAt = _clock.UtcNow };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private async Task<Guid> SeedUserAsync(SimplCalConDbContext context, Guid tenantId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = "U",
            Email = $"u-{Guid.NewGuid():N}@t.local",
            NormalizedEmail = $"U-{Guid.NewGuid():N}@T.LOCAL",
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            CreatedAt = _clock.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private sealed class CapturingNotifier : IChangeNotifier
    {
        public Guid? LastAdminTenant { get; set; }

        public Task CollectionChangedAsync(Guid collectionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvitationsChangedAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SharesChangedAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AdminChangedAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            LastAdminTenant = tenantId;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyDirectory : IPrincipalDirectory
    {
        public Task<IReadOnlyList<PrincipalSummary>> SearchAsync(Guid tenantId, string? query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrincipalSummary>>([]);

        public Task<IReadOnlyList<PrincipalSummary>> GetAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrincipalSummary>>([]);
    }
}
