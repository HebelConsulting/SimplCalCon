using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SimplCalCon.Application.Abstractions.Email;
using SimplCalCon.Application.Abstractions.Push;

namespace SimplCalCon.IntegrationTests;

/// <summary>Captures WebDAV-Push sends in tests instead of hitting a real push service (ADR 0052).</summary>
public sealed class CapturingWebPushSender : IWebPushSender
{
    private readonly ConcurrentQueue<(string Endpoint, string Payload)> _sent = new();

    public IReadOnlyCollection<(string Endpoint, string Payload)> Sent => _sent;

    public Task<WebPushDelivery> SendAsync(
        string endpoint, string p256dh, string auth, string payload, CancellationToken cancellationToken)
    {
        _sent.Enqueue((endpoint, payload));
        return Task.FromResult(WebPushDelivery.Delivered);
    }
}

/// <summary>Captures iMIP sends in tests instead of hitting a real SMTP server (ADR 0047).</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<(TenantSmtpConfig Config, ItipMail Mail)> _sent = new();
    private readonly ConcurrentQueue<(string To, string Subject)> _plain = new();

    public IReadOnlyCollection<(TenantSmtpConfig Config, ItipMail Mail)> Sent => _sent;

    public IReadOnlyCollection<(string To, string Subject)> PlainSent => _plain;

    public Task SendItipAsync(TenantSmtpConfig config, ItipMail mail, CancellationToken cancellationToken)
    {
        _sent.Enqueue((config, mail));
        return Task.CompletedTask;
    }

    public Task SendAsync(TenantSmtpConfig config, string to, string subject, string body, CancellationToken cancellationToken)
    {
        _plain.Enqueue((to, subject));
        return Task.CompletedTask;
    }
}

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
    public const string InboundApiKey = "test-inbound-secret";

    // CI can run the suite against PostgreSQL by setting these; the default is a
    // throwaway per-factory SQLite file (ADR 0001, 0024).
    private static readonly bool UsePostgres = string.Equals(
        Environment.GetEnvironmentVariable("SIMPLCALCON_TEST_DB_PROVIDER"), "Postgres", StringComparison.OrdinalIgnoreCase);

    private static readonly string? PostgresConnection =
        Environment.GetEnvironmentVariable("SIMPLCALCON_TEST_DB_CONNECTION");

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"simplcalcon-it-{Guid.NewGuid():N}.db");

    public CapturingEmailSender EmailSender { get; } = new();

    public CapturingWebPushSender WebPushSender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // Capture iMIP email + Web Push sends instead of hitting real services.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
            services.RemoveAll<IWebPushSender>();
            services.AddSingleton<IWebPushSender>(WebPushSender);
        });

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
                // Enable WebDAV-Push with an ephemeral VAPID key pair for the tests (ADR 0052).
                ["SimplCalCon:WebPush:AllowEphemeralKeys"] = "true",
                // Inbound iMIP REST endpoint shared secret (ADR 0056).
                ["SimplCalCon:InboundEmail:ApiKey"] = InboundApiKey,
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
