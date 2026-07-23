using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

public sealed class RestApiTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Discovery_document_is_public_and_links_resources()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rels = doc.RootElement.GetProperty("links").EnumerateArray()
            .Select(l => l.GetProperty("rel").GetString()).ToList();
        Assert.Contains("me", rels);
        Assert.Contains("app-passwords", rels);
    }

    [Fact]
    public async Task OpenApi_document_is_served_anonymously()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("app-passwords", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Me_requires_authentication()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_authenticated_user()
    {
        var client = await AuthenticatedClientAsync();

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(AuthWebApplicationFactory.DemoAdminEmail, doc.RootElement.GetProperty("email").GetString());
        Assert.Equal("admin", doc.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task App_password_lifecycle_with_etag_concurrency()
    {
        var client = await AuthenticatedClientAsync();

        // Create -> 201 with the one-time secret and an ETag header.
        var created = await client.PostAsJsonAsync("/api/app-passwords", new { label = "iPhone" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var etag = created.Headers.ETag!.ToString();
        Assert.False(string.IsNullOrEmpty(etag));
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrEmpty(createdBody.RootElement.GetProperty("secret").GetString()));

        // List -> present, and never exposes the secret.
        var list = await client.GetFromJsonAsync<JsonElement>("/api/app-passwords");
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetGuid() == id);
        Assert.DoesNotContain(items, i => i.TryGetProperty("secret", out _));

        // DELETE without If-Match -> 428 problem+json.
        var noPrecondition = await client.DeleteAsync($"/api/app-passwords/{id}");
        Assert.Equal(HttpStatusCode.PreconditionRequired, noPrecondition.StatusCode);
        Assert.Equal("application/problem+json", noPrecondition.Content.Headers.ContentType?.MediaType);
        Assert.Equal("IF_MATCH_REQUIRED", await ErrorCodeAsync(noPrecondition));

        // DELETE with a stale ETag -> 412.
        var stale = new HttpRequestMessage(HttpMethod.Delete, $"/api/app-passwords/{id}");
        stale.Headers.TryAddWithoutValidation("If-Match", $"\"{Guid.NewGuid()}\"");
        var staleResponse = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);
        Assert.Equal("ETAG_MISMATCH", await ErrorCodeAsync(staleResponse));

        // DELETE with the current ETag -> 204.
        var revoke = new HttpRequestMessage(HttpMethod.Delete, $"/api/app-passwords/{id}");
        revoke.Headers.TryAddWithoutValidation("If-Match", etag);
        var revokeResponse = await client.SendAsync(revoke);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        // Gone.
        var afterRevoke = await client.GetAsync($"/api/app-passwords/{id}");
        Assert.Equal(HttpStatusCode.NotFound, afterRevoke.StatusCode);
        Assert.Equal("APP_PASSWORD_NOT_FOUND", await ErrorCodeAsync(afterRevoke));
    }

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("errorCode").GetString();
    }
}
