using Microsoft.EntityFrameworkCore;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Storage;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

public sealed class RetentionServiceTests
{
    private readonly TestDatabase _database = new();

    private static readonly DateTime Cutoff = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Purges_old_trash_with_revisions_but_keeps_recent_trash_and_live()
    {
        var bookId = Guid.NewGuid();
        var oldTrash = Guid.NewGuid();
        var recentTrash = Guid.NewGuid();
        var live = Guid.NewGuid();

        await using (var seed = _database.CreateContext())
        {
            SeedOwner(seed, bookId);
            seed.ContactObjects.Add(Contact(oldTrash, bookId, "old", deletedAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            seed.ContactObjects.Add(Contact(recentTrash, bookId, "recent", deletedAt: new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
            seed.ContactObjects.Add(Contact(live, bookId, "live", deletedAt: null));
            seed.ObjectRevisions.Add(new ObjectRevision
            {
                Id = Guid.NewGuid(), ObjectId = oldTrash, RevisionNumber = 1, Blob = "x",
                Operation = RevisionOperation.Created, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            await seed.SaveChangesAsync();
        }

        int purged;
        await using (var context = _database.CreateContext())
        {
            purged = await new RetentionService(context).PurgeTrashedBeforeAsync(Cutoff, 100, default);
        }

        Assert.Equal(1, purged);
        await using var verify = _database.CreateContext();
        Assert.False(await verify.ContactObjects.AnyAsync(o => o.Id == oldTrash));
        Assert.False(await verify.ObjectRevisions.AnyAsync(r => r.ObjectId == oldTrash));
        Assert.True(await verify.ContactObjects.AnyAsync(o => o.Id == recentTrash));
        Assert.True(await verify.ContactObjects.AnyAsync(o => o.Id == live));
    }

    private static ContactObject Contact(Guid id, Guid bookId, string uid, DateTime? deletedAt) => new()
    {
        Id = id, CollectionId = bookId, Uid = uid, ResourceName = $"{uid}.vcf",
        Blob = $"BEGIN:VCARD\r\nVERSION:4.0\r\nUID:{uid}\r\nFN:{uid}\r\nEND:VCARD\r\n",
        CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        RevisionNumber = 1,
        IsDeleted = deletedAt is not null,
        DeletedAt = deletedAt,
    };

    private static void SeedOwner(SimplCalConDbContext context, Guid bookId)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "T", Slug = $"t-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UnixEpoch };
        var owner = new User
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, DisplayName = "Owner",
            Email = $"o-{Guid.NewGuid():N}@t.test", NormalizedEmail = $"O-{Guid.NewGuid():N}@T.TEST",
            SecurityStamp = Guid.NewGuid(), Status = UserStatus.Active, CreatedAt = DateTimeOffset.UnixEpoch,
        };
        context.Tenants.Add(tenant);
        context.Users.Add(owner);
        context.AddressBooks.Add(new AddressBook
        {
            Id = bookId, TenantId = tenant.Id, OwnerId = owner.Id, Name = "Contacts",
            ResourceName = "ab", CreatedAt = DateTimeOffset.UnixEpoch.UtcDateTime,
        });
    }
}
