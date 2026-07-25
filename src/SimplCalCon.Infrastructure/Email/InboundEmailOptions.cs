namespace SimplCalCon.Infrastructure.Email;

/// <summary>Bound from <c>SimplCalCon:InboundEmail</c> (ADR 0056).</summary>
public sealed class InboundEmailOptions
{
    /// <summary>Shared secret for the REST ingestion endpoint (X-Inbound-Key header); the endpoint is off when unset.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Enables the background IMAP poller (off by default — most deployments use the REST endpoint).</summary>
    public bool PollerEnabled { get; set; }

    /// <summary>Poll interval in seconds (floored at 30).</summary>
    public int PollSeconds { get; set; } = 120;
}
