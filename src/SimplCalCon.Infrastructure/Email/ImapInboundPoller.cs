using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimplCalCon.Application.Abstractions.Email;
using SimplCalCon.Application.Abstractions.Scheduling;

namespace SimplCalCon.Infrastructure.Email;

/// <summary>
/// Background poller that fetches inbound iMIP mail from each tenant's configured IMAP mailbox and
/// feeds it to <see cref="IInboundItipProcessor"/> (ADR 0056). Off by default; per-tenant and
/// per-message errors are isolated so one bad mailbox doesn't stop the cycle. Marks handled
/// messages \Seen.
/// </summary>
internal sealed class ImapInboundPoller(
    IServiceScopeFactory scopeFactory,
    IOptions<InboundEmailOptions> options,
    ILogger<ImapInboundPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PollerEnabled)
        {
            logger.LogInformation("Inbound IMAP poller disabled (SimplCalCon:InboundEmail:PollerEnabled).");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, options.Value.PollSeconds));
        logger.LogInformation("Inbound IMAP poller started ({Interval}s).", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Inbound IMAP poll cycle failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollAllAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ITenantEmailSettingsService>();
        var processor = scope.ServiceProvider.GetRequiredService<IInboundItipProcessor>();

        foreach (var tenantId in await settings.ListInboundTenantIdsAsync(cancellationToken))
        {
            if (await settings.GetImapConfigAsync(tenantId, cancellationToken) is not { } config)
            {
                continue;
            }

            try
            {
                await PollTenantAsync(config, processor, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Inbound IMAP poll failed for tenant {TenantId}.", tenantId);
            }
        }
    }

    private async Task PollTenantAsync(TenantImapConfig config, IInboundItipProcessor processor, CancellationToken cancellationToken)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(
            config.Host, config.Port,
            config.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, cancellationToken);
        if (!string.IsNullOrEmpty(config.Username))
        {
            await client.AuthenticateAsync(config.Username, config.Password ?? string.Empty, cancellationToken);
        }

        var folder = config.Folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(config.Folder, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

        foreach (var uid in await folder.SearchAsync(SearchQuery.NotSeen, cancellationToken))
        {
            var message = await folder.GetMessageAsync(uid, cancellationToken);
            using var buffer = new MemoryStream();
            await message.WriteToAsync(buffer, cancellationToken);
            var result = await processor.ProcessAsync(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), cancellationToken);
            logger.LogInformation("Inbound IMAP message {Uid} → {Outcome}.", uid, result.Outcome);
            await folder.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, cancellationToken);
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
