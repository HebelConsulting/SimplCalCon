namespace SimplCalCon.Infrastructure.Configuration;

public enum DatabaseProvider
{
    Sqlite,
    Postgres,
}

/// <summary>Selects the persistence provider and its connection string (ADR 0001).</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "SimplCalCon:Database";

    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    public string? ConnectionString { get; set; }
}
