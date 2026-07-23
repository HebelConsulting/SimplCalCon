using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.UnitTests.TestSupport;

/// <summary>
/// A throwaway SQLite database (in-memory, one connection kept open for its lifetime)
/// with the real schema created from the model. Exercises the production DbContext,
/// including its SaveChanges invariants.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public SimplCalConDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SimplCalConDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
