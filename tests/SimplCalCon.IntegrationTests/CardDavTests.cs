using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

public sealed class CardDavTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CardDav = "urn:ietf:params:xml:ns:carddav";

    private static string VCard(string uid, string fn) => $"""
        BEGIN:VCARD
        VERSION:3.0
        UID:{uid}
        FN:{fn}
        N:{fn};;;;
        EMAIL:{uid}@example.com
        END:VCARD
        """;

    [Fact]
    public async Task Discovery_returns_current_user_principal_and_home_set()
    {
        var (client, userId) = await DavClientAsync();

        var principal = await SendAsync(client, "PROPFIND", $"/dav/principals/{userId}/", depth: 0, body: """
            <propfind xmlns="DAV:" xmlns:card="urn:ietf:params:xml:ns:carddav">
              <prop><current-user-principal/><card:addressbook-home-set/></prop>
            </propfind>
            """);

        Assert.Equal(207, (int)principal.StatusCode);
        var doc = XDocument.Parse(await principal.Content.ReadAsStringAsync());
        Assert.Equal($"/dav/principals/{userId}/", doc.Descendants(Dav + "current-user-principal").Descendants(Dav + "href").First().Value);
        Assert.Equal($"/dav/addressbooks/{userId}/", doc.Descendants(CardDav + "addressbook-home-set").Descendants(Dav + "href").First().Value);
    }

    [Fact]
    public async Task Home_auto_provisions_a_default_address_book()
    {
        var (client, userId) = await DavClientAsync();

        var home = await SendAsync(client, "PROPFIND", $"/dav/addressbooks/{userId}/", depth: 1, body: PropfindBody("displayname", "resourcetype"));

        Assert.Equal(207, (int)home.StatusCode);
        var doc = XDocument.Parse(await home.Content.ReadAsStringAsync());
        Assert.Contains(doc.Descendants(Dav + "href"), h => h.Value == $"/dav/addressbooks/{userId}/contacts/");
    }

    [Fact]
    public async Task Home_provisions_the_contacts_default_even_when_other_books_exist()
    {
        var (client, userId) = await DavClientAsync();
        await CreateBookAsync(client, userId); // a non-"contacts" book (like a web-UI-created one)

        var home = await SendAsync(client, "PROPFIND", $"/dav/addressbooks/{userId}/", depth: 1, body: PropfindBody("resourcetype"));

        Assert.Equal(207, (int)home.StatusCode);
        var doc = XDocument.Parse(await home.Content.ReadAsStringAsync());
        Assert.Contains(doc.Descendants(Dav + "href"), h => h.Value == $"/dav/addressbooks/{userId}/contacts/");
    }

    [Fact]
    public async Task Root_options_advertises_carddav_capability()
    {
        // macOS Contacts (RFC 6764) probes OPTIONS on the bare root and requires a DAV
        // header advertising `addressbook`, or it discards the account.
        var (client, _) = await DavClientAsync();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("addressbook", response.Headers.GetValues("DAV").First());
    }

    [Fact]
    public async Task Root_propfind_returns_current_user_principal()
    {
        var (client, userId) = await DavClientAsync();

        var response = await SendAsync(client, "PROPFIND", "/", depth: 0, body: """
            <propfind xmlns="DAV:"><prop><current-user-principal/></prop></propfind>
            """);

        Assert.Equal(207, (int)response.StatusCode);
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            $"/dav/principals/{userId}/",
            doc.Descendants(Dav + "current-user-principal").Descendants(Dav + "href").First().Value);
    }

    [Fact]
    public async Task Proppatch_is_accepted_as_a_noop()
    {
        // Apple clients (dataaccessd) PROPPATCH collections during account setup and abort on 405.
        var (client, userId) = await DavClientAsync();
        var book = await CreateBookAsync(client, userId);

        var response = await SendAsync(client, "PROPPATCH", $"/dav/addressbooks/{userId}/{book}/",
            body: """<propertyupdate xmlns="DAV:"><set><prop><displayname>Renamed</displayname></prop></set></propertyupdate>""");

        Assert.Equal(207, (int)response.StatusCode);
        var doc = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(doc.Descendants(Dav + "status"), s => s.Value.Contains("200"));
    }

    [Fact]
    public async Task Put_get_and_conditional_headers()
    {
        var (client, userId) = await DavClientAsync();
        var book = await CreateBookAsync(client, userId);
        var url = $"/dav/addressbooks/{userId}/{book}/jane.vcf";

        var created = await SendAsync(client, "PUT", url, content: VCard("jane", "Jane Doe"), contentType: "text/vcard");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var etag = created.Headers.ETag!.ToString();

        var fetched = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Contains("FN:Jane Doe", await fetched.Content.ReadAsStringAsync());

        // If-None-Match: * on an existing resource must fail.
        var conflict = new HttpRequestMessage(HttpMethod.Put, url) { Content = TextVCard(VCard("jane", "Jane Doe")) };
        conflict.Headers.TryAddWithoutValidation("If-None-Match", "*");
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await client.SendAsync(conflict)).StatusCode);

        // If-Match with the current ETag succeeds (204).
        var update = new HttpRequestMessage(HttpMethod.Put, url) { Content = TextVCard(VCard("jane", "Jane R Doe")) };
        update.Headers.TryAddWithoutValidation("If-Match", etag);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(update)).StatusCode);
    }

    [Fact]
    public async Task Sync_collection_reports_changes_and_removals()
    {
        var (client, userId) = await DavClientAsync();
        var book = await CreateBookAsync(client, userId);

        await SendAsync(client, "PUT", $"/dav/addressbooks/{userId}/{book}/a.vcf", content: VCard("a", "A"), contentType: "text/vcard");

        // Initial sync: returns the object and a sync-token.
        var initial = await SendAsync(client, "REPORT", $"/dav/addressbooks/{userId}/{book}/", body: SyncBody(null));
        var initialDoc = XDocument.Parse(await initial.Content.ReadAsStringAsync());
        var token = initialDoc.Descendants(Dav + "sync-token").First().Value;
        Assert.Contains(initialDoc.Descendants(Dav + "href"), h => h.Value.EndsWith("/a.vcf"));

        // Change: add b, delete a.
        await SendAsync(client, "PUT", $"/dav/addressbooks/{userId}/{book}/b.vcf", content: VCard("b", "B"), contentType: "text/vcard");
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/dav/addressbooks/{userId}/{book}/a.vcf"));

        // Incremental sync from the token: b changed, a removed (404).
        var delta = await SendAsync(client, "REPORT", $"/dav/addressbooks/{userId}/{book}/", body: SyncBody(token));
        var deltaDoc = XDocument.Parse(await delta.Content.ReadAsStringAsync());
        var responses = deltaDoc.Descendants(Dav + "response").ToList();
        Assert.Contains(responses, r => r.Element(Dav + "href")!.Value.EndsWith("/b.vcf"));
        Assert.Contains(responses, r => r.Element(Dav + "href")!.Value.EndsWith("/a.vcf")
            && r.Element(Dav + "status")!.Value.Contains("404"));
    }

    [Fact]
    public async Task Multiget_returns_requested_address_data()
    {
        var (client, userId) = await DavClientAsync();
        var book = await CreateBookAsync(client, userId);
        var href = $"/dav/addressbooks/{userId}/{book}/x.vcf";
        await SendAsync(client, "PUT", href, content: VCard("x", "Xavier"), contentType: "text/vcard");

        var report = await SendAsync(client, "REPORT", $"/dav/addressbooks/{userId}/{book}/", body: $"""
            <card:addressbook-multiget xmlns:d="DAV:" xmlns:card="urn:ietf:params:xml:ns:carddav">
              <d:prop><d:getetag/><card:address-data/></d:prop>
              <d:href>{href}</d:href>
            </card:addressbook-multiget>
            """);

        var doc = XDocument.Parse(await report.Content.ReadAsStringAsync());
        Assert.Contains("FN:Xavier", doc.Descendants(CardDav + "address-data").First().Value);
    }

    private static string PropfindBody(params string[] props) =>
        $"<propfind xmlns=\"DAV:\"><prop>{string.Concat(props.Select(p => $"<{p}/>"))}</prop></propfind>";

    private static string SyncBody(string? token) => $"""
        <sync-collection xmlns="DAV:">
          <sync-token>{token}</sync-token>
          <sync-level>1</sync-level>
          <prop><getetag/></prop>
        </sync-collection>
        """;

    private static StringContent TextVCard(string vcard) => new(vcard, Encoding.UTF8, "text/vcard");

    private async Task<string> CreateBookAsync(HttpClient client, Guid userId)
    {
        var book = $"b{Guid.NewGuid():N}";
        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod("MKCOL"), $"/dav/addressbooks/{userId}/{book}"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return book;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string method, string url, string? body = null, string? content = null, string? contentType = null, int? depth = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (depth is not null)
        {
            request.Headers.Add("Depth", depth.ToString());
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/xml");
        }
        else if (content is not null)
        {
            request.Content = new StringContent(content, Encoding.UTF8, contentType ?? "text/plain");
        }

        return await client.SendAsync(request);
    }

    private async Task<(HttpClient Client, Guid UserId)> DavClientAsync() =>
        await DavTestUser.CreateAsync(factory, "carddav");
}
