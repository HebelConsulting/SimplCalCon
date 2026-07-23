using System.Net;

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
}
