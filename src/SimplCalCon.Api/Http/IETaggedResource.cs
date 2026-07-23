namespace SimplCalCon.Api.Http;

/// <summary>
/// A response resource whose optimistic-concurrency token is emitted as the HTTP
/// ETag by <see cref="ETagResultFilter"/>. Implementations mark the property
/// <c>[JsonIgnore]</c> so it travels in the header, not the body.
/// </summary>
public interface IETaggedResource
{
    Guid ConcurrencyToken { get; }
}
