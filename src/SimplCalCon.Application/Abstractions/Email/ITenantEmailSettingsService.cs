namespace SimplCalCon.Application.Abstractions.Email;

/// <summary>
/// Per-tenant SMTP settings (ADR 0047): read for sending (decrypted), read for admin display
/// (never the password value), and save (encrypting the password at rest).
/// </summary>
public interface ITenantEmailSettingsService
{
    /// <summary>The SMTP config for sending, or null when the tenant has it disabled/unconfigured.</summary>
    Task<TenantSmtpConfig?> GetSendConfigAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>The SMTP config regardless of the Enabled flag (for the "send test email" check), or null if unconfigured.</summary>
    Task<TenantSmtpConfig?> GetConfigAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>The settings for admin display — reports whether a password is set, never its value.</summary>
    Task<TenantEmailSettingsView?> GetAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Creates or updates the settings; a null <see cref="TenantEmailSettingsInput.NewPassword"/> keeps the stored one.</summary>
    Task SaveAsync(Guid tenantId, TenantEmailSettingsInput input, CancellationToken cancellationToken);

    /// <summary>The inbound IMAP config for polling (decrypted), or null when disabled/unconfigured (ADR 0056).</summary>
    Task<TenantImapConfig?> GetImapConfigAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Tenant ids with inbound IMAP enabled — for the poller to iterate (ADR 0056).</summary>
    Task<IReadOnlyList<Guid>> ListInboundTenantIdsAsync(CancellationToken cancellationToken);
}

public sealed record TenantEmailSettingsView(
    bool Enabled, string Host, int Port, bool UseStartTls, string? Username, bool HasPassword, string FromAddress, string? FromName,
    bool InboundEnabled, string? ImapHost, int ImapPort, bool ImapUseSsl, string? ImapUsername, bool HasImapPassword, string? ImapFolder);

public sealed record TenantEmailSettingsInput(
    bool Enabled, string Host, int Port, bool UseStartTls, string? Username, string? NewPassword, string FromAddress, string? FromName,
    bool InboundEnabled, string? ImapHost, int ImapPort, bool ImapUseSsl, string? ImapUsername, string? NewImapPassword, string? ImapFolder);

public sealed record TenantImapConfig(string Host, int Port, bool UseSsl, string? Username, string? Password, string Folder);
