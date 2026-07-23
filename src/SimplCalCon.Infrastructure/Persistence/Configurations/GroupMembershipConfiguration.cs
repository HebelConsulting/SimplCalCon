using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class GroupMembershipConfiguration : IEntityTypeConfiguration<GroupMembership>
{
    public void Configure(EntityTypeBuilder<GroupMembership> builder)
    {
        builder.ToTable("GroupMemberships");
        builder.HasKey(m => new { m.GroupId, m.MemberId });

        // Deleting a group removes its outgoing membership edges.
        builder.HasOne(m => m.Group)
            .WithMany(g => g.Memberships)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // A member (user or nested group) can't be deleted while still a member;
        // callers remove the edge first. Restrict also avoids a second cascade path
        // into Principals.
        builder.HasOne(m => m.Member)
            .WithMany()
            .HasForeignKey(m => m.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.MemberId);
    }
}
