using System.Text;
using SimplCalCon.Infrastructure.Storage;

namespace SimplCalCon.UnitTests;

public class VCardPhotoRefTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Parses_vcard3_inline_base64_photo()
    {
        var vcard = $"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ada\r\nPHOTO;ENCODING=b;TYPE=PNG:{Convert.ToBase64String(Png)}\r\nEND:VCARD\r\n";

        var result = VCardPhotoRef.Parse(vcard);

        var inline = Assert.IsType<VCardPhotoRef.Inline>(result);
        Assert.Equal(Png, inline.Bytes);
        Assert.Equal("image/png", inline.ContentType);
    }

    [Fact]
    public void Parses_data_uri_photo()
    {
        var vcard = $"BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Ada\r\nPHOTO:data:image/jpeg;base64,{Convert.ToBase64String(Png)}\r\nEND:VCARD\r\n";

        var inline = Assert.IsType<VCardPhotoRef.Inline>(VCardPhotoRef.Parse(vcard));
        Assert.Equal(Png, inline.Bytes);
        Assert.Equal("image/jpeg", inline.ContentType);
    }

    [Fact]
    public void Parses_external_url_photo()
    {
        var vcard = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Ada\r\nPHOTO:https://example.com/ada.jpg\r\nEND:VCARD\r\n";

        var url = Assert.IsType<VCardPhotoRef.Url>(VCardPhotoRef.Parse(vcard));
        Assert.Equal("https://example.com/ada.jpg", url.Value);
    }

    [Fact]
    public void Unfolds_a_folded_photo_url()
    {
        // RFC 6350 line folding: a leading space continues the previous line.
        var vcard = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Ada\r\nPHOTO:https://example.com/very/\r\n long/path.jpg\r\nEND:VCARD\r\n";

        var url = Assert.IsType<VCardPhotoRef.Url>(VCardPhotoRef.Parse(vcard));
        Assert.Equal("https://example.com/very/long/path.jpg", url.Value);
    }

    [Fact]
    public void Reports_none_when_no_photo()
    {
        var vcard = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Ada\r\nEND:VCARD\r\n";

        Assert.IsType<VCardPhotoRef.None>(VCardPhotoRef.Parse(vcard));
    }

    [Fact]
    public void Ignores_a_grouped_property_that_is_not_photo()
    {
        var vcard = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Ada\r\nitem1.URL:https://example.com\r\nEND:VCARD\r\n";

        Assert.IsType<VCardPhotoRef.None>(VCardPhotoRef.Parse(vcard));
    }

    [Fact]
    public void ReplacePhoto_swaps_a_url_photo_for_inline_base64()
    {
        var vcard = "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:Ada\r\nPHOTO:https://example.com/ada.jpg\r\nEND:VCARD\r\n";

        var rewritten = VCardPhotoRef.ReplacePhoto(vcard, Png, "image/png");

        Assert.DoesNotContain("https://example.com/ada.jpg", rewritten);
        // The rewrite must round-trip back to an inline photo carrying our bytes.
        var inline = Assert.IsType<VCardPhotoRef.Inline>(VCardPhotoRef.Parse(rewritten));
        Assert.Equal(Png, inline.Bytes);
        Assert.Contains("FN:Ada", rewritten);
        Assert.EndsWith("END:VCARD\r\n", rewritten);
    }

    [Fact]
    public void ReplacePhoto_drops_a_folded_inline_photo_before_inserting()
    {
        var big = Convert.ToBase64String(Encoding.ASCII.GetBytes(new string('x', 200)));
        var vcard = $"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ada\r\nPHOTO;ENCODING=b;TYPE=JPEG:{big[..60]}\r\n {big[60..]}\r\nEND:VCARD\r\n";

        var rewritten = VCardPhotoRef.ReplacePhoto(vcard, Png, "image/png");

        // Exactly one PHOTO property survives (the new one), and it is ours.
        Assert.Single(rewritten.Split("\r\n"), line => line.StartsWith("PHOTO", StringComparison.Ordinal));
        var inline = Assert.IsType<VCardPhotoRef.Inline>(VCardPhotoRef.Parse(rewritten));
        Assert.Equal(Png, inline.Bytes);
    }
}
