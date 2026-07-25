using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Push;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("PushSubscriptions");
        builder.HasKey(s => s.Id);

        builder.HasOne(s => s.Collection).WithMany()
            .HasForeignKey(s => s.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.Endpoint).IsRequired().HasMaxLength(2048);
        builder.Property(s => s.P256dh).IsRequired().HasMaxLength(256);
        builder.Property(s => s.Auth).IsRequired().HasMaxLength(128);

        // One subscription per (collection, endpoint) — re-registration updates the row.
        builder.HasIndex(s => new { s.CollectionId, s.Endpoint }).IsUnique();
    }
}
