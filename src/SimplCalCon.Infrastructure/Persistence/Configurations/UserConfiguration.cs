using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Principals;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // Columns are nullable at the store level because Group rows share the
        // Principals table (TPH); the C# `required` keyword and app logic enforce
        // presence for users.
        builder.Property(u => u.Email).HasMaxLength(320);
        builder.Property(u => u.NormalizedEmail).HasMaxLength(320);
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();

        builder.Property(u => u.PasswordHash).HasMaxLength(256);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.TenantRole).HasConversion<string>().HasMaxLength(20);

        builder.Ignore(u => u.IsPlatformAdministrator);
    }
}
