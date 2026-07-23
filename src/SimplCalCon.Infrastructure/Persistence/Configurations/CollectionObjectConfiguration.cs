using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class CollectionObjectConfiguration : IEntityTypeConfiguration<CollectionObject>
{
    public void Configure(EntityTypeBuilder<CollectionObject> builder)
    {
        builder.ToTable("Objects");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Uid).IsRequired().HasMaxLength(255);
        builder.Property(o => o.ResourceName).IsRequired().HasMaxLength(255);
        builder.Property(o => o.Blob).IsRequired();
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.UpdatedAt).IsRequired();

        builder.HasDiscriminator<string>("ObjectType")
            .HasValue<CalendarObject>("CalendarObject")
            .HasValue<ContactObject>("ContactObject");

        builder.HasOne(o => o.Collection)
            .WithMany(c => c.Objects)
            .HasForeignKey(o => o.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(o => new { o.CollectionId, o.ResourceName }).IsUnique();
        builder.HasIndex(o => new { o.CollectionId, o.Uid }).IsUnique();
        // Drives sync-collection: "objects changed since token" within a collection.
        builder.HasIndex(o => new { o.CollectionId, o.ChangeNumber });
    }
}
