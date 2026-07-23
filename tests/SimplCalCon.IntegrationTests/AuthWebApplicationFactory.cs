using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SimplCalCon.IntegrationTests;

/// <summary>
/// Boots the real Api host against a throwaway SQLite file in the Development
/// environment, so the bootstrap seeder provisions the SPA client, platform admin,
/// and demo tenant + admin used by the tests.
/// </summary>
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string SpaBaseUrl = "https://localhost:5001";
    public const string SpaClientId = "simplcalcon-spa";
    public const string RedirectUri = SpaBaseUrl + "/authentication/login-callback";

    public const string DemoAdminEmail = "admin@demo.test";
    public const string DemoAdminPassword = "Demo-Admin-Passphrase-2026";

    // CI can run the suite against PostgreSQL by setting these; the default is a
    // throwaway per-factory SQLite file (ADR 0001, 0024).
    private static readonly bool UsePostgres = string.Equals(
        Environment.GetEnvironmentVariable("SIMPLCALCON_TEST_DB_PROVIDER"), "Postgres", StringComparison.OrdinalIgnoreCase);

    private static readonly string? PostgresConnection =
        Environment.GetEnvironmentVariable("SIMPLCALCON_TEST_DB_CONNECTION");

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"simplcalcon-it-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SimplCalCon:Database:Provider"] = UsePostgres ? "Postgres" : "Sqlite",
                ["SimplCalCon:Database:ConnectionString"] =
                    UsePostgres ? PostgresConnection : $"Data Source={_databasePath}",
                ["SimplCalCon:SpaClient:BaseUrl"] = SpaBaseUrl,
                ["SimplCalCon:Bootstrap:PlatformAdmin:Email"] = "platform@simplcalcon.test",
                ["SimplCalCon:Bootstrap:PlatformAdmin:Password"] = "Platform-Admin-Passphrase-2026",
                ["SimplCalCon:Bootstrap:DemoTenant:TenantName"] = "Demo",
                ["SimplCalCon:Bootstrap:DemoTenant:TenantSlug"] = "demo",
                ["SimplCalCon:Bootstrap:DemoTenant:AdminEmail"] = DemoAdminEmail,
                ["SimplCalCon:Bootstrap:DemoTenant:AdminPassword"] = DemoAdminPassword,
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && !UsePostgres && File.Exists(_databasePath))
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the throwaway test database.
            }
        }
    }
}
