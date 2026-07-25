using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class EventOccurrenceConfiguration : IEntityTypeConfiguration<EventOccurrence>
{
    public void Configure(EntityTypeBuilder<EventOccurrence> builder)
    {
        builder.ToTable("EventOccurrences");
        builder.HasKey(o => o.Id);

        builder.HasOne(o => o.Object)
            .WithMany(o => o.Occurrences)
            .HasForeignKey(o => o.ObjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // The time-range range scan: occurrences of a collection ordered by start.
        builder.HasIndex(o => new { o.CollectionId, o.StartUtc });
        builder.HasIndex(o => o.ObjectId);
    }
}
