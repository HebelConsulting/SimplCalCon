using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SimplCalCon.Application.Abstractions.Email;

namespace SimplCalCon.Infrastructure.Email;

/// <summary>
/// Sends iMIP scheduling email over SMTP with MailKit (ADR 0047): a multipart/alternative of a short
/// text part and the iTIP <c>text/calendar; method=…</c> payload (RFC 6047).
/// </summary>
internal sealed class MailKitEmailSender : IEmailSender
{
    public async Task SendItipAsync(TenantSmtpConfig config, ItipMail mail, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(config.FromName ?? string.Empty, config.FromAddress));
        message.To.Add(MailboxAddress.Parse(mail.To));
        if (!string.IsNullOrWhiteSpace(mail.ReplyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(mail.ReplyTo));
        }

        message.Subject = mail.Subject;

        var text = new TextPart("plain") { Text = mail.TextBody };
        var calendar = new TextPart("calendar") { Text = mail.CalendarBody };
        calendar.ContentType.Charset = "utf-8";
        calendar.ContentType.Parameters["method"] = mail.Method;
        calendar.ContentType.Parameters["component"] = "VEVENT";
        message.Body = new MultipartAlternative { text, calendar };

        using var smtp = new SmtpClient();
        var socketOptions = config.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await smtp.ConnectAsync(config.Host, config.Port, socketOptions, cancellationToken);
        if (!string.IsNullOrEmpty(config.Username))
        {
            await smtp.AuthenticateAsync(config.Username, config.Password ?? string.Empty, cancellationToken);
        }

        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(quit: true, cancellationToken);
    }
}
