using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Server.AspNetCore;
using SimplCalCon.Api.Authentication;
using SimplCalCon.Infrastructure;
using SimplCalCon.Infrastructure.Configuration;
using SimplCalCon.Infrastructure.Postgres;
using SimplCalCon.Infrastructure.Sqlite;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

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
