using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SimplCalCon.IntegrationTests.TestSupport;

/// <summary>Drives the OIDC code+PKCE flow to obtain a bearer access token for the demo admin.</summary>
internal static class AuthFlow
{
    public static async Task<string> GetDemoAdminAccessTokenAsync(AuthWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = AuthWebApplicationFactory.DemoAdminEmail,
            ["password"] = AuthWebApplicationFactory.DemoAdminPassword,
            ["returnUrl"] = "/",
        }));
        login.EnsureSuccessStatusCode2xxOrRedirect();

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var authorize = await client.GetAsync(
            "/connect/authorize" +
            $"?client_id={AuthWebApplicationFactory.SpaClientId}" +
            "&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(AuthWebApplicationFactory.RedirectUri)}" +
            $"&scope={Uri.EscapeDataString("openid email profile simplcalcon.api")}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=abc");

        var code = ParseQuery(authorize.Headers.Location!.Query)["code"];

        var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = AuthWebApplicationFactory.RedirectUri,
            ["client_id"] = AuthWebApplicationFactory.SpaClientId,
            ["code_verifier"] = verifier,
        }));
        token.EnsureSuccessStatusCode();

        using var payload = JsonDocument.Parse(await token.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("access_token").GetString()!;
    }

    private static void EnsureSuccessStatusCode2xxOrRedirect(this HttpResponseMessage response)
    {
        if ((int)response.StatusCode is not (>= 200 and < 400))
        {
            throw new InvalidOperationException($"Login failed with {(int)response.StatusCode}.");
        }
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts.ElementAtOrDefault(1) ?? string.Empty));
}
