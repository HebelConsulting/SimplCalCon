namespace SimplCalCon.Application.Abstractions.Email;

/// <summary>Resolved (decrypted) SMTP settings for a tenant — enough to connect and send (ADR 0047).</summary>
public sealed record TenantSmtpConfig(
    string Host, int Port, bool UseStartTls, string? Username, string? Password, string FromAddress, string? FromName);

/// <summary>An iMIP scheduling email (RFC 6047): a short text part + the iTIP VCALENDAR payload.</summary>
public sealed record ItipMail(
    string To, string? ReplyTo, string Subject, string TextBody, string CalendarBody, string Method);

/// <summary>Sends outbound iMIP scheduling email through a tenant's SMTP server.</summary>
public interface IEmailSender
{
    Task SendItipAsync(TenantSmtpConfig config, ItipMail mail, CancellationToken cancellationToken);
}
