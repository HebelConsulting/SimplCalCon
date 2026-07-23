using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class ObjectRevisionConfiguration : IEntityTypeConfiguration<ObjectRevision>
{
    public void Configure(EntityTypeBuilder<ObjectRevision> builder)
    {
        builder.ToTable("ObjectRevisions");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Blob).IsRequired();
        builder.Property(r => r.Operation).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();

        builder.HasOne(r => r.Object)
            .WithMany(o => o.Revisions)
            .HasForeignKey(r => r.ObjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.ObjectId, r.RevisionNumber }).IsUnique();
    }
}
