namespace SimplCalCon.Client.Services;

/// <summary>
/// Tracks whether the pending login redirect was caused by session expiry (ADR 0079), so the login page
/// can show a brief "your session expired" banner. In-memory for the app instance; the flag is set by
/// <see cref="SessionExpiredHandler"/> just before it redirects and read (and cleared) by the login page.
/// </summary>
public sealed class SessionState
{
    public bool SessionExpired { get; set; }
}
