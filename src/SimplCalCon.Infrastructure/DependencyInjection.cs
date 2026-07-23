using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Application.Abstractions.Security;
using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Infrastructure.Bootstrap;
using SimplCalCon.Infrastructure.Storage;
using SimplCalCon.Infrastructure.Configuration;
using SimplCalCon.Infrastructure.Identity;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.Infrastructure.Security;
using SimplCalCon.Infrastructure.Time;

namespace SimplCalCon.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence, the auth services, and OpenIddict's core (EF) stores.
    /// The provider (PostgreSQL/SQLite) is supplied by the caller via
    /// <paramref name="configureDatabase"/> so the provider packages stay in the Api
    /// host (ADR 0001, 0017). The OpenIddict ASP.NET server/validation integration is
    /// added by the Api host, which owns the signing keys.
    /// </summary>
    public static IServiceCollection AddSimplCalConInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.Configure<LockoutOptions>(configuration.GetSection(LockoutOptions.SectionName));
        services.Configure<PasswordPolicyOptions>(configuration.GetSection(PasswordPolicyOptions.SectionName));
        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        services.Configure<SpaClientOptions>(configuration.GetSection(SpaClientOptions.SectionName));

        services.AddDbContext<SimplCalConDbContext>(options =>
        {
            configureDatabase(options);
            options.UseOpenIddict();
        });

        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<SimplCalConDbContext>());

        services.AddMemoryCache();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<PasswordHashing>();
        services.AddSingleton<IPasswordPolicy, DefaultPasswordPolicy>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
        services.AddScoped<IAppPasswordService, AppPasswordService>();
        services.AddScoped<IAccountActivationService, AccountActivationService>();
        services.AddScoped<IDavCredentialAuthenticator, DavCredentialAuthenticator>();
        services.AddScoped<IObjectStore, ObjectStore>();
        services.AddScoped<IObjectImportExport, ObjectImportExport>();
        services.AddScoped<IDavRepository, DavRepository>();

        services.AddHostedService<BootstrapHostedService>();

        return services;
    }
}
