using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class AclEntryConfiguration : IEntityTypeConfiguration<AclEntry>
{
    public void Configure(EntityTypeBuilder<AclEntry> builder)
    {
        builder.ToTable("AclEntries");
        builder.HasKey(e => e.Id);

        // Flags value stored as int (bitwise-queryable on both providers).
        builder.Property(e => e.Rights).HasConversion<int>();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasOne(e => e.Collection)
            .WithMany()
            .HasForeignKey(e => e.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Principal)
            .WithMany()
            .HasForeignKey(e => e.PrincipalId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.CollectionId, e.PrincipalId }).IsUnique();
        builder.HasIndex(e => e.PrincipalId);
    }
}
