namespace SimplCalCon.Domain.Tenants;

/// <summary>
/// Per-tenant outbound SMTP configuration for iMIP email delivery (ADR 0047). A 1:1 shared-PK
/// companion to <see cref="Tenant"/> (<see cref="TenantId"/> is both PK and FK). The SMTP password
/// is stored reversibly-encrypted (ASP.NET Data Protection) so it can authenticate to the server.
/// </summary>
public class TenantEmailSettings
{
    public Guid TenantId { get; set; }

    public Tenant? Tenant { get; set; }

    /// <summary>When false, external attendees are logged/dropped (no email sent).</summary>
    public bool Enabled { get; set; }

    public required string Host { get; set; }

    public int Port { get; set; }

    public bool UseStartTls { get; set; }

    public string? Username { get; set; }

    /// <summary>Data-Protection-encrypted SMTP password; null for an unauthenticated relay.</summary>
    public string? PasswordEncrypted { get; set; }

    public required string FromAddress { get; set; }

    public string? FromName { get; set; }

    // --- Inbound iMIP over IMAP (ADR 0056) ---

    /// <summary>When true, the poller fetches iMIP mail from the mailbox below.</summary>
    public bool InboundEnabled { get; set; }

    public string? ImapHost { get; set; }

    public int ImapPort { get; set; } = 993;

    public bool ImapUseSsl { get; set; } = true;

    public string? ImapUsername { get; set; }

    /// <summary>Data-Protection-encrypted IMAP password.</summary>
    public string? ImapPasswordEncrypted { get; set; }

    public string? ImapFolder { get; set; }
}
