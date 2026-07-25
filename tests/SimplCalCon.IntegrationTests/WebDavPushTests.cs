using System.Net;
using System.Text;
using System.Xml.Linq;
using SimplCalCon.IntegrationTests.TestSupport;

namespace SimplCalCon.IntegrationTests;

/// <summary>WebDAV-Push (ADR 0052): capability advertisement, registration, and change fan-out to the push endpoint.</summary>
public sealed class WebDavPushTests(AuthWebApplicationFactory factory) : IClassFixture<AuthWebApplicationFactory>
{
    private static readonly XNamespace Push = "https://bitfire.at/webdav-push";

    [Fact]
    public async Task Propfind_advertises_the_web_push_transport_and_topic()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "push-caps");
        var cal = await CreateCalendarAsync(client, userId);

        var response = await SendAsync(client, "PROPFIND", $"/dav/calendars/{userId}/{cal}/", depth: 0, body:
            """
            <propfind xmlns="DAV:" xmlns:P="https://bitfire.at/webdav-push">
              <prop><P:transports/><P:topic/></prop>
            </propfind>
            """);

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotNull(xml.Descendants(Push + "web-push").FirstOrDefault());
        Assert.NotNull(xml.Descendants(Push + "vapid-public-key").FirstOrDefault());
        Assert.False(string.IsNullOrWhiteSpace(xml.Descendants(Push + "topic").FirstOrDefault()?.Value));
    }

    [Fact]
    public async Task Register_then_write_pushes_to_the_endpoint_with_topic_and_sync_token()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "push-deliver");
        var cal = await CreateCalendarAsync(client, userId);
        var collectionPath = $"/dav/calendars/{userId}/{cal}/";

        // The advertised topic — the push payload must carry the same value.
        var propfind = await SendAsync(client, "PROPFIND", collectionPath, depth: 0, body:
            """<propfind xmlns="DAV:" xmlns:P="https://bitfire.at/webdav-push"><prop><P:topic/></prop></propfind>""");
        var topic = XDocument.Parse(await propfind.Content.ReadAsStringAsync())
            .Descendants(Push + "topic").First().Value;

        var endpoint = $"https://push.example.test/ep-{Guid.NewGuid():N}";
        var register = await SendAsync(client, "POST", collectionPath, body: RegisterBody(endpoint, expires: "Wed, 20 Dec 2028 10:03:31 GMT"));
        Assert.Equal(HttpStatusCode.NoContent, register.StatusCode);
        Assert.NotNull(register.Headers.Location);
        Assert.NotNull(register.Content.Headers.Expires); // server-decided expiration (IMF-fixdate)

        var before = factory.WebPushSender.Sent.Count;

        // A write bumps the collection change sequence → a push to the subscription.
        await SendAsync(client, "PUT", $"{collectionPath}ev.ics",
            content: Event("push-ev@t", "Pushed", "20260715T090000Z"), contentType: "text/calendar");

        var sent = factory.WebPushSender.Sent.ToList();
        Assert.True(sent.Count > before, "Expected a Web Push send after the write.");
        var mine = sent.Last(s => s.Endpoint == endpoint);
        Assert.Contains(topic, mine.Payload);
        Assert.Contains("sync-token", mine.Payload);
    }

    [Fact]
    public async Task Unregister_stops_further_pushes()
    {
        var (client, userId) = await DavTestUser.CreateAsync(factory, "push-unreg");
        var cal = await CreateCalendarAsync(client, userId);
        var collectionPath = $"/dav/calendars/{userId}/{cal}/";

        var endpoint = $"https://push.example.test/ep-{Guid.NewGuid():N}";
        var register = await SendAsync(client, "POST", collectionPath, body: RegisterBody(endpoint, expires: null));
        var location = register.Headers.Location!.AbsolutePath; // /dav/push-subscriptions/{id}

        var unregister = await SendAsync(client, "DELETE", location);
        Assert.Equal(HttpStatusCode.NoContent, unregister.StatusCode);

        var before = factory.WebPushSender.Sent.Count(s => s.Endpoint == endpoint);
        await SendAsync(client, "PUT", $"{collectionPath}ev2.ics",
            content: Event("unreg-ev@t", "After", "20260716T090000Z"), contentType: "text/calendar");

        Assert.Equal(before, factory.WebPushSender.Sent.Count(s => s.Endpoint == endpoint));
    }

    private static string RegisterBody(string endpoint, string? expires) =>
        $"""
        <push-register xmlns="https://bitfire.at/webdav-push" xmlns:D="DAV:">
          <subscription>
            <web-push-subscription>
              <push-resource>{endpoint}</push-resource>
              <content-encoding>aes128gcm</content-encoding>
              <subscription-public-key type="p256dh">BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4</subscription-public-key>
              <auth-secret>BTBZMqHH6r4Tts7J_aSIgg</auth-secret>
            </web-push-subscription>
          </subscription>
          {(expires is null ? "" : $"<expires>{expires}</expires>")}
        </push-register>
        """;

    private static async Task<string> CreateCalendarAsync(HttpClient client, Guid userId)
    {
        var name = $"pushcal-{Guid.NewGuid():N}";
        await SendAsync(client, "MKCALENDAR", $"/dav/calendars/{userId}/{name}/");
        return name;
    }

    private static string Event(string uid, string summary, string start) =>
        $"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\nUID:{uid}\r\n" +
        $"DTSTAMP:{start}\r\nDTSTART:{start}\r\nSUMMARY:{summary}\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string method, string url, string? body = null, string? content = null,
        string? contentType = null, int? depth = null)
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
}
