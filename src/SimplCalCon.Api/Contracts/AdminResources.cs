namespace SimplCalCon.Api.Contracts;

/// <summary>A tenant, for the platform-admin view (ADR 0034).</summary>
public sealed record TenantResource(Guid Id, string Name, string Slug, string Status);

/// <summary>A user within a tenant, for the tenant-admin view (ADR 0034). <c>HasPhoto</c> drives the list thumbnail (ADR 0035).</summary>
public sealed record AdminUserResource(Guid Id, string DisplayName, string Email, string Role, string Status, bool HasPhoto);

/// <summary>A tenant's SMTP/iMIP settings for the tenant-admin view (ADR 0047/0056); passwords are never returned.</summary>
public sealed record TenantEmailSettingsResource(
    bool Enabled, string Host, int Port, bool UseStartTls, string? Username, bool HasPassword, string FromAddress, string? FromName,
    bool InboundEnabled, string? ImapHost, int ImapPort, bool ImapUseSsl, string? ImapUsername, bool HasImapPassword, string? ImapFolder);

/// <summary>Write a tenant's SMTP + inbound IMAP settings (ADR 0047/0056). New*Password: null keeps the stored one, "" clears it.</summary>
public sealed class TenantEmailSettingsWriteRequest
{
    public bool Enabled { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public bool UseStartTls { get; init; } = true;

    public string? Username { get; init; }

    public string? NewPassword { get; init; }

    public string FromAddress { get; init; } = string.Empty;

    public string? FromName { get; init; }

    // Inbound IMAP (ADR 0056).
    public bool InboundEnabled { get; init; }

    public string? ImapHost { get; init; }

    public int ImapPort { get; init; } = 993;

    public bool ImapUseSsl { get; init; } = true;

    public string? ImapUsername { get; init; }

    public string? NewImapPassword { get; init; }

    public string? ImapFolder { get; init; }
}

/// <summary>Send a test email to verify a tenant's SMTP settings (ADR 0047).</summary>
public sealed class TestEmailRequest
{
    public string To { get; init; } = string.Empty;
}

/// <summary>A tenant group and its member count, for the admin view (ADR 0059).</summary>
public sealed record GroupResource(Guid Id, string Name, int MemberCount);

/// <summary>A member of a group (user or nested group), ADR 0059.</summary>
public sealed record GroupMemberResource(Guid Id, string Kind, string DisplayName, string? Email);

public sealed class CreateGroupRequest
{
    public string Name { get; init; } = string.Empty;
}
