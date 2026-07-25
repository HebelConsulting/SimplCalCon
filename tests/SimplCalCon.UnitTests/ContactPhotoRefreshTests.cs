using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Objects;
using SimplCalCon.Domain.Principals;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Storage;
using SimplCalCon.UnitTests.TestSupport;

namespace SimplCalCon.UnitTests;

public sealed class ContactPhotoRefreshTests
{
    private readonly TestDatabase _database = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    // A dead source URL (connection refused instantly) so the fetch fails and the cache self-heals.
    private const string DeadUrl = "http://127.0.0.1:1/photo.jpg";

    private const string CardWithUrlPhoto =
        "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:photo-c\r\nFN:Jane Doe\r\nPHOTO:" + DeadUrl + "\r\nEND:VCARD\r\n";

    [Fact]
    public async Task Refresh_self_heals_a_dead_url_by_embedding_the_cached_photo()
    {
        var bookId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var now = _clock.UtcNow.UtcDateTime;

        await using (var seed = _database.CreateContext())
        {
            var (tenantId, ownerId) = await SeedOwnerAsync(seed, now);
            seed.AddressBooks.Add(new AddressBook
            {
                Id = bookId, TenantId = tenantId, OwnerId = ownerId,
                Name = "Contacts", ResourceName = "ab", CreatedAt = now,
            });
            seed.ContactObjects.Add(new ContactObject
            {
                Id = contactId, CollectionId = bookId, Uid = "photo-c", ResourceName = "c.vcf",
                Blob = CardWithUrlPhoto, CreatedAt = now, UpdatedAt = now, RevisionNumber = 1,
            });
            seed.ContactPhotos.Add(new ContactPhoto
            {
                ObjectId = contactId, Photo = [1, 2, 3, 4], ContentType = "image/png",
                SourceUrl = DeadUrl, FetchedAt = now.AddDays(-30), // stale (> 7 days)
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var objectStore = new ObjectStore(context, _clock, NullLogger<ObjectStore>.Instance, new NoOpChangeNotifier());
            var service = new ContactPhotoService(
                context, objectStore, new StubHttpClientFactory(), _clock, NullLogger<ContactPhotoService>.Instance);

            Assert.Equal(1, await service.RefreshStaleAsync(10, default));
        }

        await using (var verify = _database.CreateContext())
        {
            // The dead URL was self-healed: the cache row is gone and the card now carries the photo inline.
            Assert.False(await verify.ContactPhotos.AnyAsync(p => p.ObjectId == contactId));
            var blob = await verify.ContactObjects.Where(o => o.Id == contactId).Select(o => o.Blob).FirstAsync();
            Assert.IsType<VCardPhotoRef.Inline>(VCardPhotoRef.Parse(blob));
        }
    }

    [Fact]
    public async Task Refresh_deletes_an_orphaned_cache_when_the_card_no_longer_references_a_url()
    {
        var bookId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var now = _clock.UtcNow.UtcDateTime;

        await using (var seed = _database.CreateContext())
        {
            var (tenantId, ownerId) = await SeedOwnerAsync(seed, now);
            seed.AddressBooks.Add(new AddressBook
            {
                Id = bookId, TenantId = tenantId, OwnerId = ownerId,
                Name = "Contacts", ResourceName = "ab", CreatedAt = now,
            });
            seed.ContactObjects.Add(new ContactObject
            {
                Id = contactId, CollectionId = bookId, Uid = "no-photo", ResourceName = "c.vcf",
                Blob = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:no-photo\r\nFN:Bob\r\nEND:VCARD\r\n",
                CreatedAt = now, UpdatedAt = now, RevisionNumber = 1,
            });
            seed.ContactPhotos.Add(new ContactPhoto
            {
                ObjectId = contactId, Photo = [1], ContentType = "image/png",
                SourceUrl = DeadUrl, FetchedAt = now.AddDays(-30),
            });
            await seed.SaveChangesAsync();
        }

        await using (var context = _database.CreateContext())
        {
            var service = new ContactPhotoService(
                context, new ObjectStore(context, _clock, NullLogger<ObjectStore>.Instance, new NoOpChangeNotifier()),
                new StubHttpClientFactory(), _clock, NullLogger<ContactPhotoService>.Instance);
            await service.RefreshStaleAsync(10, default);
        }

        await using var verify = _database.CreateContext();
        Assert.False(await verify.ContactPhotos.AnyAsync(p => p.ObjectId == contactId));
    }

    private async Task<(Guid TenantId, Guid OwnerId)> SeedOwnerAsync(SimplCalConDbContext context, DateTime now)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "T", Slug = $"t-{Guid.NewGuid():N}", CreatedAt = _clock.UtcNow };
        var owner = new User
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, DisplayName = "Owner",
            Email = $"o-{Guid.NewGuid():N}@t.test", NormalizedEmail = $"O-{Guid.NewGuid():N}@T.TEST",
            SecurityStamp = Guid.NewGuid(), Status = UserStatus.Active, CreatedAt = _clock.UtcNow,
        };
        context.Tenants.Add(tenant);
        context.Users.Add(owner);
        return await Task.FromResult((tenant.Id, owner.Id));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromSeconds(2) };
    }
}
