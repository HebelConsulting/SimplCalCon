using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class EventAttendeeConfiguration : IEntityTypeConfiguration<EventAttendee>
{
    public void Configure(EntityTypeBuilder<EventAttendee> builder)
    {
        builder.ToTable("EventAttendees");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Address).HasMaxLength(320).IsRequired();
        builder.Property(a => a.NormalizedAddress).HasMaxLength(320).IsRequired();
        builder.Property(a => a.CommonName).HasMaxLength(256);
        builder.Property(a => a.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(a => a.ParticipationStatus).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasOne(a => a.Object)
            .WithMany(o => o.Attendees)
            .HasForeignKey(a => a.ObjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.ObjectId);
        builder.HasIndex(a => a.NormalizedAddress);
    }
}
