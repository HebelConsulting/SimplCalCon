using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.Property(g => g.NormalizedName).HasMaxLength(200);

        // Group names are unique within their tenant (case-insensitive via the
        // normalized column). User rows leave both columns null; null-distinct
        // index semantics on both providers keep them out of the constraint.
        builder.HasIndex(nameof(Principal.TenantId), nameof(Group.NormalizedName)).IsUnique();
    }
}
