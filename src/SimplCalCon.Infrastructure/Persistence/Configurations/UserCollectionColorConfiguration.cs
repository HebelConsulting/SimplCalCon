using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Collections;
using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class UserCollectionColorConfiguration : IEntityTypeConfiguration<UserCollectionColor>
{
    public void Configure(EntityTypeBuilder<UserCollectionColor> builder)
    {
        builder.ToTable("UserCollectionColors");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Color).HasMaxLength(32).IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Collection>().WithMany().HasForeignKey(c => c.CollectionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.UserId, c.CollectionId }).IsUnique();
    }
}
