using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Live updates over SignalR (ADR 0049): hub auth + the write-path change push.</summary>
public sealed class LiveUpdatesTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Hub_rejects_an_anonymous_connection()
    {
        await using var connection = BuildConnection(accessToken: null);
        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    [Fact]
    public async Task Writing_an_event_pushes_collection_changed_to_a_subscriber()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var calendarId = (await Body(await client.PostAsJsonAsync(
            "/api/calendars", new { name = $"Live {Guid.NewGuid():N}" }))).GetProperty("id").GetGuid();

        await using var connection = BuildConnection(token);
        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<Guid>("CollectionChanged", id => received.TrySetResult(id));

        await connection.StartAsync();
        await connection.InvokeAsync("Subscribe", calendarId);

        // A write bumps the change sequence and must push CollectionChanged to the subscriber.
        await client.PostAsJsonAsync($"/api/calendars/{calendarId}/events", new
        {
            summary = "Pushed",
            startUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            endUtc = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
            isAllDay = false,
        });

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(completed == received.Task, "Timed out waiting for the CollectionChanged push.");
        Assert.Equal(calendarId, await received.Task);
    }

    // LongPolling over the in-memory TestServer handler (the bearer token rides the Authorization
    // header on every poll; real browsers use WebSockets with the token in the query string).
    private HubConnection BuildConnection(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hub/notifications"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
