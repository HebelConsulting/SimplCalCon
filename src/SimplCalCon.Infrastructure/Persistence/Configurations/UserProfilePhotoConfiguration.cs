using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class UserProfilePhotoConfiguration : IEntityTypeConfiguration<UserProfilePhoto>
{
    public void Configure(EntityTypeBuilder<UserProfilePhoto> builder)
    {
        builder.ToTable("UserProfilePhotos");

        // Shared primary key: UserId is both PK and the FK to Users (1:1), cascading on delete.
        builder.HasKey(p => p.UserId);

        builder.HasOne(p => p.User)
            .WithOne()
            .HasForeignKey<UserProfilePhoto>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tenant-scoped like everything else; restrict so a tenant can't be deleted out from under a photo.
        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.Photo).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        builder.HasIndex(p => p.TenantId);
    }
}
