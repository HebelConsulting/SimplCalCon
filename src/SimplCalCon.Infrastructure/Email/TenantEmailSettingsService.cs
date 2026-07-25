using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SimplCalCon.Application.Abstractions.Email;
using SimplCalCon.Domain.Tenants;
using SimplCalCon.Infrastructure.Persistence;

namespace SimplCalCon.Infrastructure.Email;

/// <summary>Reads/writes per-tenant SMTP settings, encrypting the password with Data Protection (ADR 0047).</summary>
internal sealed class TenantEmailSettingsService : ITenantEmailSettingsService
{
    private readonly SimplCalConDbContext dbContext;
    private readonly IDataProtector protector;

    public TenantEmailSettingsService(SimplCalConDbContext dbContext, IDataProtectionProvider dataProtection)
    {
        this.dbContext = dbContext;
        protector = dataProtection.CreateProtector("SimplCalCon.TenantEmailSettings.SmtpPassword.v1");
    }

    public async Task<TenantSmtpConfig?> GetSendConfigAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.TenantEmailSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (settings is null || !settings.Enabled)
        {
            return null;
        }

        return ToConfig(settings);
    }

    public async Task<TenantSmtpConfig?> GetConfigAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.TenantEmailSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        return settings is null ? null : ToConfig(settings);
    }

    private TenantSmtpConfig ToConfig(TenantEmailSettings settings) => new(
        settings.Host, settings.Port, settings.UseStartTls, settings.Username,
        Decrypt(settings.PasswordEncrypted), settings.FromAddress, settings.FromName);

    public async Task<TenantEmailSettingsView?> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.TenantEmailSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        return settings is null
            ? null
            : new TenantEmailSettingsView(
                settings.Enabled, settings.Host, settings.Port, settings.UseStartTls, settings.Username,
                settings.PasswordEncrypted is not null, settings.FromAddress, settings.FromName);
    }

    public async Task SaveAsync(Guid tenantId, TenantEmailSettingsInput input, CancellationToken cancellationToken)
    {
        var settings = await dbContext.TenantEmailSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        if (settings is null)
        {
            settings = new TenantEmailSettings { TenantId = tenantId, Host = input.Host, FromAddress = input.FromAddress };
            dbContext.TenantEmailSettings.Add(settings);
        }

        settings.Enabled = input.Enabled;
        settings.Host = input.Host;
        settings.Port = input.Port;
        settings.UseStartTls = input.UseStartTls;
        settings.Username = input.Username;
        settings.FromAddress = input.FromAddress;
        settings.FromName = input.FromName;

        // null NewPassword keeps the stored one; empty clears it; otherwise (re)encrypt.
        if (input.NewPassword is not null)
        {
            settings.PasswordEncrypted = input.NewPassword.Length == 0 ? null : protector.Protect(input.NewPassword);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string? Decrypt(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
        {
            return null;
        }

        try
        {
            return protector.Unprotect(encrypted);
        }
        catch (CryptographicException)
        {
            return null; // keys rotated/lost (e.g. dev ephemeral keys after a restart)
        }
    }
}
