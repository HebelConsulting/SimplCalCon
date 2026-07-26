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

    [Fact]
    public async Task Prunes_old_revisions_beyond_the_keep_minimum_but_keeps_recent_and_the_floor()
    {
        var bookId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var old = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);      // before Cutoff (2025-06-01)
        var recent = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);  // after Cutoff

        await using (var seed = _database.CreateContext())
        {
            SeedOwner(seed, bookId);
            var contact = Contact(contactId, bookId, "c", deletedAt: null);
            contact.RevisionNumber = 10;   // the live counter = 10 revisions (1..10)
            seed.ContactObjects.Add(contact);
            for (long n = 1; n <= 10; n++)
            {
                seed.ObjectRevisions.Add(new ObjectRevision
                {
                    Id = Guid.NewGuid(), ObjectId = contactId, RevisionNumber = n, Blob = "x",
                    ETag = Guid.NewGuid(),
                    Operation = n == 1 ? RevisionOperation.Created : RevisionOperation.Updated,
                    CreatedAt = n <= 8 ? old : recent,   // 1-8 old, 9-10 recent
                });
            }
            await seed.SaveChangesAsync();
        }

        int processed;
        await using (var context = _database.CreateContext())
        {
            processed = await new RetentionService(context).PruneRevisionsAsync(Cutoff, keepMinimum: 3, batchSize: 100, default);
        }

        Assert.Equal(1, processed);
        await using var verify = _database.CreateContext();
        var remaining = await verify.ObjectRevisions
            .Where(r => r.ObjectId == contactId).Select(r => r.RevisionNumber).OrderBy(n => n).ToListAsync();
        // 1-7 pruned (old AND outside the last 3); #8 kept by the keep-min floor despite being old; 9-10 recent.
        Assert.Equal([8L, 9L, 10L], remaining);
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
