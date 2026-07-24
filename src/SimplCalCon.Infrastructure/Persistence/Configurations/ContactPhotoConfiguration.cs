using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class ContactPhotoConfiguration : IEntityTypeConfiguration<ContactPhoto>
{
    public void Configure(EntityTypeBuilder<ContactPhoto> builder)
    {
        builder.ToTable("ContactPhotos");

        // Shared primary key: ObjectId is both PK and the FK to the contact object (1:1), cascading on delete.
        builder.HasKey(p => p.ObjectId);

        builder.HasOne(p => p.Object)
            .WithOne()
            .HasForeignKey<ContactPhoto>(p => p.ObjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.Photo).IsRequired();
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.SourceUrl).IsRequired();
        builder.Property(p => p.FetchedAt).IsRequired();

        builder.HasIndex(p => p.TenantId);
    }
}
