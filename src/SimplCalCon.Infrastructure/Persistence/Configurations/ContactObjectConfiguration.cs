using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SimplCalCon.Domain.Objects;

namespace SimplCalCon.Infrastructure.Persistence.Configurations;

public class ContactObjectConfiguration : IEntityTypeConfiguration<ContactObject>
{
    public void Configure(EntityTypeBuilder<ContactObject> builder)
    {
        builder.Property(o => o.FormattedName).HasMaxLength(512);
        builder.Property(o => o.FamilyName).HasMaxLength(256);
        builder.Property(o => o.GivenName).HasMaxLength(256);
        builder.Property(o => o.Organization).HasMaxLength(512);
        builder.Property(o => o.Emails).HasMaxLength(2048);
        builder.Property(o => o.Phones).HasMaxLength(2048);

        builder.HasIndex(nameof(CollectionObject.CollectionId), nameof(ContactObject.FamilyName));
    }
}
