namespace SimplCalCon.Domain.Common;

/// <summary>
/// Marks an entity that carries an optimistic-concurrency token surfaced as the
/// resource ETag. The token is configured as an EF Core concurrency token and is
/// regenerated to a fresh <see cref="System.Guid"/> on every insert/update by the
/// DbContext — never set it by hand. See docs/adr/0009-rest-api-conventions.md.
/// </summary>
public interface IHasConcurrencyToken
{
    Guid ConcurrencyToken { get; set; }
}
