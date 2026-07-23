using SimplCalCon.Domain.Acl;
using SimplCalCon.Domain.Acl.Exceptions;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Storage;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

public sealed class AclServiceTests
{
    private const AclRight All =
        AclRight.Read | AclRight.WriteContent | AclRight.Create | AclRight.Delete | AclRight.Share | AclRight.Admin;

    private readonly TestDatabase _database = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private AclService Service() => new(_database.CreateContext(), _clock);

    [Fact]
    public async Task Owner_has_all_rights()
    {
        var (tenantId, ownerId) = await SeedTenantWithUserAsync();
        var bookId = await SeedAddressBookAsync(tenantId, ownerId);

        Assert.Equal(All, await Service().GetEffectiveRightsAsync(ownerId, bookId, default));
    }

    [Fact]
    public async Task Stranger_has_no_rights()
    {
        var (tenantId, ownerId) = await SeedTenantWithUserAsync();
        var bookId = await SeedAddressBookAsync(tenantId, ownerId);
        var strangerId = await SeedUserAsync(tenantId);

        Assert.Equal(AclRight.None, await Service().GetEffectiveRightsAsync(strangerId, bookId, default));
    }

    [Fact]
    public async Task Direct_grant_confers_rights()
    {
        var (tenantId, ownerId) = await SeedTenantWithUserAsync();
        var bookId = await SeedAddressBookAsync(tenantId, ownerId);
        var shareeId = await SeedUserAsync(tenantId);

        await Service().GrantAsync(bookId, shareeId, AclRight.Read | AclRight.WriteContent, default);

        Assert.Equal(AclRight.Read | AclRight.WriteContent, await Service().GetEffectiveRightsAsync(shareeId, bookId, default));
    }

    [Fact]
    public async Task Nested_group_grant_is_transitive()
    {
        var (tenantId, ownerId) = await SeedTenantWithUserAsync();
        var bookId = await SeedAddressBookAsync(tenantId, ownerId);
        var shareeId = await SeedUserAsync(tenantId);
        var innerGroupId = await SeedGroupAsync(tenantId, "inner");
        var outerGroupId = await SeedGroupAsync(tenantId, "outer");
        await AddMemberAsync(innerGroupId, shareeId);
        await AddMemberAsync(outerGroupId, innerGroupId);

        await Service().GrantAsync(bookId, outerGroupId, AclRight.Read, default);

        Assert.True((await Service().GetEffectiveRightsAsync(shareeId, bookId, default)).HasFlag(AclRight.Read));
    }

    [Fact]
    public async Task Cross_tenant_grant_is_rejected()
    {
        var (tenantId, ownerId) = await SeedTenantWithUserAsync();
        var bookId = await SeedAddressBookAsync(tenantId, ownerId);
        var (_, otherTenantUserId) = await SeedTenantWithUserAsync();

        await Assert.ThrowsAsync<CrossTenantGrantException>(() =>
            Service().GrantAsync(bookId, otherTenantUserId, AclRight.Read, default));
    }

    private async Task<(Guid TenantId, Guid UserId)> SeedTenantWithUserAsync()
    {
        await using var context = _database.CreateContext();
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Tenant",
            Slug = $"t-{Guid.NewGuid():N}",
            CreatedAt = _clock.UtcNow,
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return (tenant.Id, await SeedUserAsync(tenant.Id));
    }

    private async Task<Guid> SeedUserAsync(Guid tenantId)
    {
        await using var context = _database.CreateContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = "User",
            Email = $"u-{Guid.NewGuid():N}@t.test",
            NormalizedEmail = $"U-{Guid.NewGuid():N}@T.TEST",
            SecurityStamp = Guid.NewGuid(),
            Status = UserStatus.Active,
            CreatedAt = _clock.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedGroupAsync(Guid tenantId, string name)
    {
        await using var context = _database.CreateContext();
        var group = new Group
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = name,
            NormalizedName = $"{name.ToUpperInvariant()}-{Guid.NewGuid():N}",
            CreatedAt = _clock.UtcNow,
        };
        context.Groups.Add(group);
        await context.SaveChangesAsync();
        return group.Id;
    }

    private async Task AddMemberAsync(Guid groupId, Guid memberId)
    {
        await using var context = _database.CreateContext();
        context.GroupMemberships.Add(new GroupMembership { GroupId = groupId, MemberId = memberId });
        await context.SaveChangesAsync();
    }

    private async Task<Guid> SeedAddressBookAsync(Guid tenantId, Guid ownerId)
    {
        await using var context = _database.CreateContext();
        var book = new AddressBook
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerId = ownerId,
            Name = "Contacts",
            ResourceName = $"ab-{Guid.NewGuid():N}",
            CreatedAt = _clock.UtcNow.UtcDateTime,
        };
        context.AddressBooks.Add(book);
        await context.SaveChangesAsync();
        return book.Id;
    }
}
