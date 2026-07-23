using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Sqlite;

/// <summary>
/// Design-time factory used by `dotnet ef` to build the context for the SQLite
/// provider. The connection string is a placeholder — generating migrations needs a
/// configured provider, not a live database. Migrations for this provider live in
/// this assembly (ADR 0001: migrations are maintained per provider).
/// </summary>
public class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SimplCalConDbContext>
{
    public SimplCalConDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SimplCalConDbContext>()
            .UseSqlite(
                "Data Source=simplcalcon.db",
                sqlite => sqlite.MigrationsAssembly(typeof(SqliteDesignTimeDbContextFactory).Assembly.FullName))
            .UseOpenIddict()
            .Options;

        return new SimplCalConDbContext(options);
    }
}
