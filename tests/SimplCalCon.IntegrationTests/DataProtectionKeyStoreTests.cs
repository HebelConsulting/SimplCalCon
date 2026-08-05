using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

// ADR 0083: the Data Protection key ring is persisted to the DbContext so it is not
// ephemeral (which would make DP-encrypted tenant SMTP/IMAP passwords undecryptable
// after a restart). Guard that a protect operation actually writes a key into the
// DataProtectionKeys table and that the value round-trips.
public sealed class DataProtectionKeyStoreTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public void Data_protection_keys_are_persisted_to_the_database()
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

        // Protecting forces key-ring creation, which the DbContext store must persist.
        var protector = provider.CreateProtector("SimplCalCon.Tests.DataProtection");
        var cipher = protector.Protect("secret-value");
        Assert.Equal("secret-value", protector.Unprotect(cipher));

        var db = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        Assert.True(db.DataProtectionKeys.AsNoTracking().Any(),
            "Data Protection key ring should be persisted in the DataProtectionKeys table, not ephemeral.");
    }
}
