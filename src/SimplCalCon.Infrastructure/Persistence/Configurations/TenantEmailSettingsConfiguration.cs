using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Tenants;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class TenantEmailSettingsConfiguration : IEntityTypeConfiguration<TenantEmailSettings>
{
    public void Configure(EntityTypeBuilder<TenantEmailSettings> builder)
    {
        builder.ToTable("TenantEmailSettings");

        // Shared primary key: TenantId is both PK and the FK to the tenant (1:1), cascading on delete.
        builder.HasKey(s => s.TenantId);
        builder.HasOne(s => s.Tenant)
            .WithOne()
            .HasForeignKey<TenantEmailSettings>(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.Host).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Username).HasMaxLength(320);
        builder.Property(s => s.FromAddress).IsRequired().HasMaxLength(320);
        builder.Property(s => s.FromName).HasMaxLength(200);

        // Inbound IMAP (ADR 0056).
        builder.Property(s => s.ImapHost).HasMaxLength(255);
        builder.Property(s => s.ImapUsername).HasMaxLength(320);
        builder.Property(s => s.ImapFolder).HasMaxLength(255);
    }
}
