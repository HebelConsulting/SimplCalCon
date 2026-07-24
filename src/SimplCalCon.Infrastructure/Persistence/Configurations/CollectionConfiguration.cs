using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Collections;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.ResourceName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasDiscriminator<string>("CollectionType")
            .HasValue<Calendar>("Calendar")
            .HasValue<AddressBook>("AddressBook")
            .HasValue<ScheduleInbox>("ScheduleInbox");

        builder.HasOne(c => c.Tenant)
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Owner)
            .WithMany()
            .HasForeignKey(c => c.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.OwnerId, c.ResourceName }).IsUnique();
        builder.HasIndex(c => c.TenantId);
    }
}
