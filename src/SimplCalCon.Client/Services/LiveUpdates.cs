using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR.Client;

namespace SimplCalCon.Client.Services;

/// <summary>
/// Manages the live-update SignalR connection (ADR 0049): connects to <c>/hub/notifications</c>
/// with the OIDC access token, raises collection/invitation change events, and <b>debounces</b>
/// bursts so a bulk import fires one <see cref="CollectionChanged"/> per collection (not one per
/// object). Best-effort — if the connection can't be established the UI still works with manual
/// refresh. Registered once (scoped == singleton in WASM).
/// </summary>
public sealed class LiveUpdates(NavigationManager navigation, IAccessTokenProvider tokenProvider) : IAsyncDisposable
{
    private const int DebounceMs = 300;

    private HubConnection? connection;
    private readonly Dictionary<Guid, CancellationTokenSource> pending = [];
    private readonly HashSet<Guid> subscribed = [];

    /// <summary>A subscribed collection changed (already debounced). Raised on the WASM UI thread.</summary>
    public event Action<Guid>? CollectionChanged;

    /// <summary>The user's schedule-inbox changed (invitation arrived or was drained).</summary>
    public event Action? InvitationsChanged;

    /// <summary>A sharing grant affecting the user changed (ADR 0064) — reload "shared with/by me".</summary>
    public event Action? SharesChanged;

    public async Task StartAsync()
    {
        if (connection is not null)
        {
            return;
        }

        connection = new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri("hub/notifications"), options =>
                options.AccessTokenProvider = async () =>
                {
                    var result = await tokenProvider.RequestAccessToken();
                    return result.TryGetToken(out var token) ? token.Value : null;
                })
            .WithAutomaticReconnect()
            .Build();

        connection.On<Guid>("CollectionChanged", DebounceCollectionChanged);
        connection.On("InvitationsChanged", () => InvitationsChanged?.Invoke());
        connection.On("SharesChanged", () => SharesChanged?.Invoke());

        // Group membership is per-connection, so re-join every subscribed collection on reconnect.
        connection.Reconnected += async _ =>
        {
            foreach (var id in subscribed.ToList())
            {
                await TryInvokeAsync("Subscribe", id);
            }
        };

        try
        {
            await connection.StartAsync();
            // Flush any collections a page subscribed to before the connection came up.
            foreach (var id in subscribed.ToList())
            {
                await TryInvokeAsync("Subscribe", id);
            }
        }
        catch
        {
            // Best-effort; the shell falls back to refresh-on-navigation.
        }
    }

    /// <summary>Join a collection's change group (call when a page starts showing it). Idempotent.</summary>
    public async Task SubscribeAsync(Guid collectionId)
    {
        subscribed.Add(collectionId);
        await TryInvokeAsync("Subscribe", collectionId);
    }

    private async Task TryInvokeAsync(string method, object arg)
    {
        if (connection is { State: HubConnectionState.Connected })
        {
            try
            {
                await connection.InvokeAsync(method, arg);
            }
            catch
            {
                // Transient — reconnect logic re-subscribes.
            }
        }
    }

    // Coalesce a burst: (re)start a short timer per collection; only the last change raises the event.
    private void DebounceCollectionChanged(Guid collectionId)
    {
        if (pending.Remove(collectionId, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }

        var cts = new CancellationTokenSource();
        pending[collectionId] = cts;
        _ = DelayThenRaiseAsync(collectionId, cts.Token);
    }

    private async Task DelayThenRaiseAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceMs, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        pending.Remove(collectionId);
        CollectionChanged?.Invoke(collectionId);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var cts in pending.Values)
        {
            cts.Dispose();
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }
}
