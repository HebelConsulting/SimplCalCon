using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Storage;

/// <summary>Per-user collection colour overrides (ADR 0066), upserted by (user, collection).</summary>
internal sealed class UserCollectionColorService(SimplCalConDbContext dbContext) : IUserCollectionColorService
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetOverridesAsync(
        Guid userId, IReadOnlyCollection<Guid> collectionIds, CancellationToken cancellationToken) =>
        await dbContext.UserCollectionColors
            .Where(c => c.UserId == userId && collectionIds.Contains(c.CollectionId))
            .ToDictionaryAsync(c => c.CollectionId, c => c.Color, cancellationToken);

    public async Task<string?> GetOverrideAsync(Guid userId, Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.UserCollectionColors
            .Where(c => c.UserId == userId && c.CollectionId == collectionId)
            .Select(c => c.Color)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SetAsync(Guid userId, Guid collectionId, string color, CancellationToken cancellationToken)
    {
        var existing = await dbContext.UserCollectionColors
            .FirstOrDefaultAsync(c => c.UserId == userId && c.CollectionId == collectionId, cancellationToken);

        if (existing is null)
        {
            dbContext.UserCollectionColors.Add(new UserCollectionColor
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CollectionId = collectionId,
                Color = color,
            });
        }
        else
        {
            existing.Color = color;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(Guid userId, Guid collectionId, CancellationToken cancellationToken) =>
        await dbContext.UserCollectionColors
            .Where(c => c.UserId == userId && c.CollectionId == collectionId)
            .ExecuteDeleteAsync(cancellationToken);
}
