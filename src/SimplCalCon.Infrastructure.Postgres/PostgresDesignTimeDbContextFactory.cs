using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Postgres;

/// <summary>
/// Design-time factory used by `dotnet ef` to build the context for the PostgreSQL
/// provider. The connection string is a placeholder — generating migrations needs a
/// configured provider, not a live database. Migrations for this provider live in
/// this assembly (ADR 0001: migrations are maintained per provider).
/// </summary>
public class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SimplCalConDbContext>
{
    public SimplCalConDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SimplCalConDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=simplcalcon;Username=simplcalcon;Password=simplcalcon",
                npgsql => npgsql.MigrationsAssembly(typeof(PostgresDesignTimeDbContextFactory).Assembly.FullName))
            .Options;

        return new SimplCalConDbContext(options);
    }
}
