using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Client.Services;

namespace SimplCalCon.WebTests;

/// <summary>
/// Guards the centralized session-expiry handler (ADR 0076): a 401 (expired/rejected token) redirects to
/// login, a success passes through untouched, and it doesn't loop while already on the auth pages.
/// </summary>
public sealed class SessionExpiredHandlerTests : TestContext
{
    private sealed class StubInner(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(code));
    }

    private static async Task<HttpResponseMessage> SendAsync(NavigationManager nav, HttpStatusCode inner, SessionState? state = null)
    {
        var handler = new SessionExpiredHandler(nav, state ?? new SessionState()) { InnerHandler = new StubInner(inner) };
        using var invoker = new HttpMessageInvoker(handler);
        return await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/me"), CancellationToken.None);
    }

    [Fact]
    public async Task Redirects_to_login_on_401_and_flags_session_expired()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var state = new SessionState();

        await SendAsync(nav, HttpStatusCode.Unauthorized, state);

        Assert.Contains("authentication/login", nav.Uri);
        Assert.True(state.SessionExpired); // the login page reads this to show the banner
    }

    [Fact]
    public async Task Passes_a_success_through_without_navigating()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var before = nav.Uri;

        var response = await SendAsync(nav, HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, nav.Uri);
    }

    [Fact]
    public async Task Does_not_redirect_again_when_already_on_an_auth_page()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("authentication/login");

        await SendAsync(nav, HttpStatusCode.Unauthorized);

        Assert.EndsWith("authentication/login", nav.Uri); // unchanged — no extra redirect appended
    }
}
