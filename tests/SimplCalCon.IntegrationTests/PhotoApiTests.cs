using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>Profile-photo round-trip + guard (ADR 0035), as the demo admin (self).</summary>
public sealed class PhotoApiTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    [Fact]
    public async Task Upload_read_flag_and_delete_round_trip()
    {
        var client = await AuthorizedClientAsync();

        // No photo yet.
        Assert.False(await HasPhotoAsync(client));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/users/me/photo")).StatusCode);

        // Upload a valid PNG.
        var put = await client.PutAsync("/api/users/me/photo", Png(256, 256));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        // /api/me reports it, and the bytes come back as image/png.
        Assert.True(await HasPhotoAsync(client));
        var get = await client.GetAsync("/api/users/me/photo");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);

        // Delete removes it.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/users/me/photo")).StatusCode);
        Assert.False(await HasPhotoAsync(client));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/users/me/photo")).StatusCode);
    }

    [Fact]
    public async Task Rejects_non_png_bytes()
    {
        var client = await AuthorizedClientAsync();
        var content = new ByteArrayContent([1, 2, 3, 4]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await client.PutAsync("/api/users/me/photo", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Photo_endpoints_require_authentication()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me/photo")).StatusCode);
    }

    private async Task<HttpClient> AuthorizedClientAsync()
    {
        var token = await AuthFlow.GetDemoAdminAccessTokenAsync(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<bool> HasPhotoAsync(HttpClient client)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/me"));
        return doc.RootElement.GetProperty("hasPhoto").GetBoolean();
    }

    private static ByteArrayContent Png(uint width, uint height)
    {
        var bytes = new byte[24];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        bytes[16] = (byte)(width >> 24); bytes[17] = (byte)(width >> 16); bytes[18] = (byte)(width >> 8); bytes[19] = (byte)width;
        bytes[20] = (byte)(height >> 24); bytes[21] = (byte)(height >> 16); bytes[22] = (byte)(height >> 8); bytes[23] = (byte)height;
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }
}
