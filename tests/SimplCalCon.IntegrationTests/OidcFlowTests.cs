using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SimplCalCon.IntegrationTests;

public sealed class OidcFlowTests(AuthWebApplicationFactory factory)
    : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Discovery_document_advertises_the_endpoints()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("authorization_endpoint", body);
        Assert.Contains("token_endpoint", body);
    }

    [Fact]
    public async Task Login_page_is_served()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Full_code_pkce_flow_yields_a_usable_access_token()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // 1. Establish the cookie session via the login form.
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = AuthWebApplicationFactory.DemoAdminEmail,
            ["password"] = AuthWebApplicationFactory.DemoAdminPassword,
            ["returnUrl"] = "/",
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        // 2. Authorization request with PKCE -> authorization code.
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var authorize = await client.GetAsync(
            "/connect/authorize" +
            $"?client_id={AuthWebApplicationFactory.SpaClientId}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(AuthWebApplicationFactory.RedirectUri)}" +
            $"&scope={Uri.EscapeDataString("openid email profile simplcalcon.api")}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=abc");

        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var location = authorize.Headers.Location!;
        Assert.StartsWith(AuthWebApplicationFactory.RedirectUri, location.GetLeftPart(UriPartial.Path));
        var code = ParseQuery(location.Query)["code"];

        // 3. Exchange the code (+ verifier) for tokens.
        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = AuthWebApplicationFactory.RedirectUri,
            ["client_id"] = AuthWebApplicationFactory.SpaClientId,
            ["code_verifier"] = verifier,
        }));
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);

        using var payload = JsonDocument.Parse(await token.Content.ReadAsStringAsync());
        var accessToken = payload.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrEmpty(accessToken));
        var idTokenSubject = JwtSubject(payload.RootElement.GetProperty("id_token").GetString()!);

        // 4. Use the access token at the userinfo endpoint.
        var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        userInfoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var userInfo = await client.SendAsync(userInfoRequest);

        Assert.Equal(HttpStatusCode.OK, userInfo.StatusCode);
        using var userInfoBody = JsonDocument.Parse(await userInfo.Content.ReadAsStringAsync());
        Assert.Equal(AuthWebApplicationFactory.DemoAdminEmail, userInfoBody.RootElement.GetProperty("email").GetString());
        // userinfo MUST return `sub` equal to the id_token subject, or OIDC clients (the SPA)
        // hang after a successful token exchange (ADR 0018).
        Assert.Equal(idTokenSubject, userInfoBody.RootElement.GetProperty("sub").GetString());
    }

    private static string JwtSubject(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(part));
        return doc.RootElement.GetProperty("sub").GetString()!;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts.ElementAtOrDefault(1) ?? string.Empty));
}
