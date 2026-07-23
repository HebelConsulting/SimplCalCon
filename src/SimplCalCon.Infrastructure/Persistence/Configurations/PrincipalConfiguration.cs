using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class PrincipalConfiguration : IEntityTypeConfiguration<Principal>
{
    public void Configure(EntityTypeBuilder<Principal> builder)
    {
        builder.ToTable("Principals");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.CreatedAt).IsRequired();

        // Table-per-hierarchy: User and Group share this table (one clean FK target
        // for ownership and ACL grants — ADR 0007).
        builder.HasDiscriminator<string>("PrincipalType")
            .HasValue<User>("User")
            .HasValue<Group>("Group");

        // Null tenant marks a platform administrator; deleting a tenant is a
        // deliberate admin operation, so block accidental cascade.
        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.TenantId);
    }
}
