using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Email;
using SimplCalCon.Application.Abstractions.Identity;
using SimplCalCon.Application.Abstractions.Scheduling;
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
        services.AddScoped<IContactPhotoService, ContactPhotoService>();

        // Fetches external contact-photo URLs (ADR 0037). Guards against SSRF at connect time,
        // caps the response, and follows only a couple of redirects.
        services.AddHttpClient(ContactPhotoService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                client.MaxResponseContentBufferSize = 5 * 1024 * 1024;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SimplCalCon/1.0 (+contact-photo-cache)");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ConnectCallback = SsrfSafeConnect.ConnectAsync,
            });
        services.AddScoped<IAccountTakeout, AccountTakeout>();
        services.AddScoped<IFreeBusyService, FreeBusyService>();
        services.AddScoped<IScheduleInboxRepository, ScheduleInboxRepository>();
        services.AddScoped<ISchedulingService, SchedulingService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<ITenantEmailSettingsService, Email.TenantEmailSettingsService>();
        services.AddScoped<IEmailSender, Email.MailKitEmailSender>();
        services.AddScoped<IObjectComposer, ObjectComposer>();
        services.AddScoped<IEventSplitter, EventSplitter>();
        services.AddScoped<IDavRepository, DavRepository>();
        services.AddScoped<IAclService, AclService>();
        services.AddScoped<IPrincipalDirectory, PrincipalDirectory>();

        services.AddHostedService<BootstrapHostedService>();

        return services;
    }
}
