using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Storage;

namespace SimplCalCon.UnitTests.TestSupport;

/// <summary>Builds the object-store services over a shared context for tests (mirrors the DI wiring).</summary>
internal static class StoreFactory
{
    public static OccurrenceIndexer Indexer(SimplCalConDbContext context, OccurrenceOptions? options = null) =>
        new(context, Options.Create(options ?? new OccurrenceOptions()));

    public static ObjectStore ObjectStore(
        SimplCalConDbContext context, IClock clock, OccurrenceOptions? options = null) =>
        new(context, clock, NullLogger<ObjectStore>.Instance, new NoOpChangeNotifier(), Indexer(context, options));
}
