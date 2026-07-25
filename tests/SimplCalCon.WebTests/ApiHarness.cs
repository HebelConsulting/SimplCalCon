using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Client.Services;

namespace SimplCalCon.WebTests;

/// <summary>
/// Shared bUnit harness (ADR 0063): registers an <see cref="ApiClient"/> backed by canned GET
/// responses, a never-called token stub, and the real <see cref="LiveUpdates"/> (which stays inert
/// without a hub connection), plus loose JS interop and an authorized user — everything a page needs
/// to render against a fake <c>/api</c>.
/// </summary>
internal static class ApiHarness
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes items into the `{ "items": [...] }` collection envelope the client expects.</summary>
    public static string List(params object[] items) => JsonSerializer.Serialize(new { items }, Web);

    public static void UseFakeApi(this TestContext ctx, Dictionary<string, string> getByPath)
    {
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(new ApiClient(
            new HttpClient(new FakeApiHandler(getByPath)) { BaseAddress = new Uri("http://localhost/") }));
        ctx.Services.AddSingleton<IAccessTokenProvider, StubTokenProvider>();
        ctx.Services.AddSingleton<LiveUpdates>();
        ctx.AddTestAuthorization().SetAuthorized("tester");
    }

    private sealed class FakeApiHandler(Dictionary<string, string> getByPath) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.Method == HttpMethod.Get && getByPath.TryGetValue(request.RequestUri!.AbsolutePath, out var body)
                ? body
                : "{\"items\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    // LiveUpdates only touches this once it has a live hub connection, which never happens in a render
    // test (a page calls SubscribeAsync, which no-ops while disconnected).
    private sealed class StubTokenProvider : IAccessTokenProvider
    {
        public ValueTask<AccessTokenResult> RequestAccessToken() => throw new NotSupportedException();

        public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options) =>
            throw new NotSupportedException();
    }
}
