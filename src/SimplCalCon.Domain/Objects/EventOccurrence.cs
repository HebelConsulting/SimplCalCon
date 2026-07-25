namespace SimplCalCon.Domain.Objects;

/// <summary>
/// A materialized occurrence of a recurring event within a rolling window (ADR 0061): the
/// occurrence-window index. One row per expanded instance, so time-range queries become an indexed
/// range scan instead of expanding the RRULE per request. Rebuilt on every write and rolled forward
/// by a background job; queries that reach beyond a row's object's materialized window fall back to
/// on-the-fly expansion, so the index is a pure acceleration.
/// </summary>
public class EventOccurrence
{
    public Guid Id { get; set; }

    /// <summary>The recurring calendar object this occurrence belongs to — FK to the objects table (cascade).</summary>
    public Guid ObjectId { get; set; }

    public CalendarObject Object { get; set; } = null!;

    /// <summary>Denormalized owning collection, so range scans filter without a join.</summary>
    public Guid CollectionId { get; set; }

    public DateTime StartUtc { get; set; }

    public DateTime EndUtc { get; set; }
}
