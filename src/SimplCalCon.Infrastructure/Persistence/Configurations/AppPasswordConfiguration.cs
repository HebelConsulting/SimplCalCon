using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Authentication;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class AppPasswordConfiguration : IEntityTypeConfiguration<AppPassword>
{
    public void Configure(EntityTypeBuilder<AppPassword> builder)
    {
        builder.ToTable("AppPasswords");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Label).IsRequired().HasMaxLength(100);
        builder.Property(a => a.PasswordHash).IsRequired().HasMaxLength(256);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne(a => a.User)
            .WithMany(u => u.AppPasswords)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.UserId);
    }
}
