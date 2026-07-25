using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class CalendarObjectConfiguration : IEntityTypeConfiguration<CalendarObject>
{
    public void Configure(EntityTypeBuilder<CalendarObject> builder)
    {
        builder.Property(o => o.ComponentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.Summary).HasMaxLength(1024);
        builder.Property(o => o.Location).HasMaxLength(1024);
        builder.Property(o => o.RecurrenceRule).HasMaxLength(1024);

        // Occurrence-window index state (ADR 0061). Default false so existing rows (backfilled by the
        // migration) and any un-materialized object are treated as "not covered" → time-range queries
        // fall back to on-the-fly expansion until the roll-forward sweep materializes them. Never
        // reporting "covered" without rows is the safe direction.
        builder.Property(o => o.OccurrencesComplete).HasDefaultValue(false);

        // Time-range queries over master start times (UTC). Contact rows leave these null.
        builder.HasIndex(nameof(CollectionObject.CollectionId), nameof(CalendarObject.DtStartUtc));
    }
}
