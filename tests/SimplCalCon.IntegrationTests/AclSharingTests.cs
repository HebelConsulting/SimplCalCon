using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Acl;
using SimplCalCon.Infrastructure.Persistence;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

public sealed class AclSharingTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly XNamespace Dav = "DAV:";

    private const string Card = """
        BEGIN:VCARD
        VERSION:3.0
        UID:jane@t
        FN:Jane Doe
        N:Doe;Jane;;;
        END:VCARD
        """;

    [Fact]
    public async Task Sharing_grants_read_then_write_and_denies_others()
    {
        var (ownerClient, ownerId) = await DavTestUser.CreateAsync(factory, "acl-owner");
        var (shareeClient, shareeId) = await DavTestUser.CreateAsync(factory, "acl-sharee");
        var (strangerClient, _) = await DavTestUser.CreateAsync(factory, "acl-stranger");

        var book = $"shared{Guid.NewGuid():N}";
        var ownerCollection = $"/dav/addressbooks/{ownerId}/{book}";
        var card = $"{ownerCollection}/jane.vcf";

        Assert.Equal(HttpStatusCode.Created, (await Send(ownerClient, "MKCOL", ownerCollection)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Send(ownerClient, "PUT", card, Card, "text/vcard")).StatusCode);

        // Before any grant: the sharee is denied.
        Assert.Equal(HttpStatusCode.Forbidden, (await shareeClient.GetAsync(card)).StatusCode);

        var bookId = await BookIdAsync(ownerId, book);
        await GrantAsync(bookId, shareeId, AclRight.Read);

        // The shared collection now appears in the sharee's home-set, at the owner's URL.
        var home = await Send(shareeClient, "PROPFIND", $"/dav/addressbooks/{shareeId}/", depth: 1,
            content: "<propfind xmlns=\"DAV:\"><prop><resourcetype/></prop></propfind>");
        var homeDoc = XDocument.Parse(await home.Content.ReadAsStringAsync());
        Assert.Contains(homeDoc.Descendants(Dav + "href"), h => h.Value == $"{ownerCollection}/");

        // Read is allowed; write is not (read-only grant).
        Assert.Equal(HttpStatusCode.OK, (await shareeClient.GetAsync(card)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(shareeClient, "PUT", card, Card, "text/vcard")).StatusCode);

        // Upgrade to write-content: the sharee can now update.
        await GrantAsync(bookId, shareeId, AclRight.Read | AclRight.WriteContent);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(shareeClient, "PUT", card, Card.Replace("Jane Doe", "Jane R Doe"), "text/vcard")).StatusCode);

        // A third user with no grant is still denied.
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync(card)).StatusCode);
    }

    private async Task GrantAsync(Guid collectionId, Guid principalId, AclRight rights)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAclService>()
            .GrantAsync(collectionId, principalId, rights, default);
    }

    private async Task<Guid> BookIdAsync(Guid ownerId, string resourceName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SimplCalConDbContext>();
        return await dbContext.AddressBooks
            .Where(a => a.OwnerId == ownerId && a.ResourceName == resourceName)
            .Select(a => a.Id)
            .FirstAsync();
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, string method, string url, string? content = null, string? contentType = null, int? depth = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (depth is not null)
        {
            request.Headers.Add("Depth", depth.ToString());
        }

        if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, contentType ?? "application/xml");
        }

        return await client.SendAsync(request);
    }
}
