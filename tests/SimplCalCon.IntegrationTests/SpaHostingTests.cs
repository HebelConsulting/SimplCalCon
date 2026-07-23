using System.Net;
using System.Text.RegularExpressions;

namespace SimplCalCon.IntegrationTests;

public sealed class SpaHostingTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Root_serves_the_spa_index()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<div id=\"app\">", body);
    }

    [Fact]
    public async Task Spa_bootstrapper_reference_resolves_to_a_served_file()
    {
        var client = factory.CreateClient();
        var html = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // The blazor.webassembly script reference must point at a real, served file. Guards the
        // hosting gotcha (ADR 0025/0030): with asset fingerprinting on, index.html keeps a raw
        // `#[.{fingerprint}]` placeholder that MapFallbackToFile serves unresolved → the SPA 404s
        // its bootstrapper and never boots. Fingerprinting is disabled so the name stays plain.
        var match = Regex.Match(html, "src=\"(_framework/blazor\\.webassembly[^\"]*)\"");
        Assert.True(match.Success, "index.html should reference the blazor.webassembly bootstrapper");

        var src = match.Groups[1].Value;
        Assert.DoesNotContain("fingerprint", src);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/" + src)).StatusCode);
    }
}
