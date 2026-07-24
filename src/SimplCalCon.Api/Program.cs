using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenIddict.Server.AspNetCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using SimplCalCon.Api.Authentication;
using SimplCalCon.Api.Errors;
using SimplCalCon.Api.Health;
using SimplCalCon.Api.Http;
using SimplCalCon.Infrastructure;
using SimplCalCon.Infrastructure.Configuration;
using SimplCalCon.Infrastructure.Postgres;
using SimplCalCon.Infrastructure.Sqlite;
using static OpenIddict.Abstractions.OpenIddictConstants;

// Two-stage Serilog init (ADR 0033): a bootstrap logger captures anything that fails
// while the host is being built; once the container is up, the real logger is
// configured from IServiceProvider + configuration and replaces it.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog owns application logging (ADR 0033). Levels come from the "Serilog"
    // configuration section; the sink is human-readable in Development and compact
    // JSON everywhere else (container/cluster log collectors parse the latter).
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "SimplCalCon");

        if (context.HostingEnvironment.IsDevelopment())
        {
            configuration.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        }
        else
        {
            configuration.WriteTo.Console(new CompactJsonFormatter());
        }
    });

    builder.Services.AddControllers(options => options.Filters.Add<ETagResultFilter>());
    builder.Services.AddOpenApi();

    // Liveness (/health/live) reports process health; readiness (/health/ready) also
    // checks the database (ADR 0024). Kubernetes and the Docker HEALTHCHECK target these.
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

    // RFC 7807 Problem Details for every error (ADR 0009).
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

    // Persistence + auth services. The provider is chosen here (the host owns the
    // provider packages and migrations assemblies); Infrastructure stays agnostic.
    var database = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
        ?? new DatabaseOptions();

    builder.Services.AddSimplCalConInfrastructure(builder.Configuration, options =>
    {
        switch (database.Provider)
        {
            case DatabaseProvider.Postgres:
                options.UseNpgsql(
                    database.ConnectionString
                        ?? throw new InvalidOperationException("A PostgreSQL connection string is required."),
                    npgsql => npgsql.MigrationsAssembly(typeof(PostgresDesignTimeDbContextFactory).Assembly.FullName));
                break;

            default:
                options.UseSqlite(
                    database.ConnectionString ?? "Data Source=simplcalcon.db",
                    sqlite => sqlite.MigrationsAssembly(typeof(SqliteDesignTimeDbContextFactory).Assembly.FullName));
                break;
        }
    });

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "SimplCalCon.Auth";
            options.LoginPath = "/Account/Login";
            options.ExpireTimeSpan = TimeSpan.FromHours(1);
            options.SlidingExpiration = true;
        })
        .AddScheme<AuthenticationSchemeOptions, DavBasicAuthenticationHandler>(
            DavAuthenticationDefaults.Scheme, _ => { });

    builder.Services.AddAuthorization();

    builder.Services.AddOpenIddict()
        .AddServer(options =>
        {
            options.SetAuthorizationEndpointUris("connect/authorize")
                .SetTokenEndpointUris("connect/token")
                .SetEndSessionEndpointUris("connect/logout")
                .SetUserInfoEndpointUris("connect/userinfo");

            options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles, "simplcalcon.api");

            options.AllowAuthorizationCodeFlow()
                .RequireProofKeyForCodeExchange()
                .AllowRefreshTokenFlow();

            options.SetAccessTokenLifetime(TimeSpan.FromMinutes(15));

            if (builder.Environment.IsDevelopment())
            {
                // Ephemeral in-memory keys: zero-setup local dev (tokens don't survive a restart).
                options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
            }
            else
            {
                AddProductionKeys(options, builder.Configuration);
            }

            var aspNetCore = options.UseAspNetCore()
                .EnableAuthorizationEndpointPassthrough()
                .EnableTokenEndpointPassthrough()
                .EnableEndSessionEndpointPassthrough()
                .EnableUserInfoEndpointPassthrough();

            if (builder.Environment.IsDevelopment())
            {
                // Allow the OIDC endpoints over plain HTTP for local dev / in-process tests.
                aspNetCore.DisableTransportSecurityRequirement();
            }
        })
        .AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
        });

    var app = builder.Build();

    // One structured summary line per request (method, path, status, elapsed, user).
    // Health probes drop to Debug so readiness polling doesn't bury the signal (ADR 0033).
    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, _, exception) => exception is not null || httpContext.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : httpContext.Request.Path.StartsWithSegments("/health")
                ? LogEventLevel.Debug
                : LogEventLevel.Information;

        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "unknown");
            if (httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId)
            {
                diagnosticContext.Set("UserId", userId);
            }
        };
    });

    app.UseExceptionHandler();

    // Serve the Blazor WASM client's framework + static files; unmatched paths fall through
    // to the SPA index at the end of the pipeline (ADR 0009/0010).
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

    app.MapFallbackToFile("index.html");

    Log.Information(
        "SimplCalCon starting ({Environment}, {Provider} database).",
        app.Environment.EnvironmentName, database.Provider);
    app.Run();
    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    // Anything that reaches here left the service unable to start: fatal (ADR 0033).
    Log.Fatal(exception, "SimplCalCon terminated unexpectedly during startup.");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

// Production requires explicit signing + encryption certificates so tokens survive
// restarts and can be rotated; fail fast if they're missing (ADR 0018).
static void AddProductionKeys(OpenIddictServerBuilder options, IConfiguration configuration)
{
    var section = configuration.GetSection("SimplCalCon:OpenIddict");
    var signingPath = section["SigningCertificatePath"];
    var encryptionPath = section["EncryptionCertificatePath"];
    var password = section["CertificatePassword"];

    if (string.IsNullOrWhiteSpace(signingPath) || string.IsNullOrWhiteSpace(encryptionPath))
    {
        throw new InvalidOperationException(
            "Production requires SimplCalCon:OpenIddict:SigningCertificatePath and EncryptionCertificatePath.");
    }

    options.AddSigningCertificate(X509CertificateLoader.LoadPkcs12FromFile(signingPath, password));
    options.AddEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(encryptionPath, password));
}

/// <summary>Exposed so the integration-test host (WebApplicationFactory) can target this assembly.</summary>
public partial class Program;
