using SimplCalCon.Application.Abstractions.Storage;
using SimplCalCon.Infrastructure.Storage;

namespace SimplCalCon.UnitTests;

/// <summary>
/// Guards the lossless structured contact editor (ADR 0082): the merge updates only the modelled fields
/// and leaves everything else (PHOTO, X-*, extra properties) intact; Read round-trips the rich fields.
/// </summary>
public sealed class ContactCardComposerTests
{
    private readonly IContactCardComposer composer = new ContactCardComposer();

    private const string RichCard =
        "BEGIN:VCARD\r\n" +
        "VERSION:3.0\r\n" +
        "UID:abc-123\r\n" +
        "FN:Jane Doe\r\n" +
        "N:Doe;Jane;;;\r\n" +
        "ORG:Acme Inc\r\n" +
        "TITLE:Engineer\r\n" +
        "EMAIL;TYPE=WORK:jane@acme.test\r\n" +
        "EMAIL;TYPE=HOME:jane@home.test\r\n" +
        "TEL;TYPE=CELL:+15551234\r\n" +
        "ADR;TYPE=HOME:;;1 Main St;Springfield;IL;62704;USA\r\n" +
        "BDAY:1990-04-01\r\n" +
        "URL:https://jane.example\r\n" +
        "NOTE:Met at a conference\r\n" +
        "PHOTO;ENCODING=b;TYPE=JPEG:/9j/EMBEDDEDBASE64DATA==\r\n" +
        "X-CUSTOM-FIELD:keep me\r\n" +
        "END:VCARD\r\n";

    [Fact]
    public void Read_extracts_the_rich_fields()
    {
        var card = composer.Read(RichCard);

        Assert.Equal("Jane Doe", card.FormattedName);
        Assert.Equal("Doe", card.FamilyName);
        Assert.Equal("Jane", card.GivenName);
        Assert.Equal("Acme Inc", card.Organization);
        Assert.Equal("Engineer", card.Title);
        Assert.Equal(["jane@acme.test", "jane@home.test"], card.Emails.Select(e => e.Value));
        Assert.Equal(["work", "home"], card.Emails.Select(e => e.Type));
        Assert.Equal(("+15551234", "mobile"), (card.Phones[0].Value, card.Phones[0].Type));
        var adr = Assert.Single(card.Addresses);
        Assert.Equal(("1 Main St", "Springfield", "IL", "62704", "USA"), (adr.Street, adr.City, adr.Region, adr.PostalCode, adr.Country));
        Assert.Equal("1990-04-01", card.Birthday);
        Assert.Equal("https://jane.example", card.Url);
        Assert.Equal("Met at a conference", card.Note);
    }

    [Fact]
    public void Merge_updates_modelled_fields_and_preserves_everything_else()
    {
        var edited = composer.Read(RichCard) with
        {
            FormattedName = "Jane Smith",
            FamilyName = "Smith",
            Organization = "Globex",
            Emails = [new ContactField("jane@globex.test", "work")],
        };

        var merged = composer.Merge(RichCard, edited, "abc-123");

        // Modelled fields reflect the edit.
        Assert.Contains("FN:Jane Smith", merged);
        Assert.Contains("N:Smith;Jane;;;", merged);
        Assert.Contains("ORG:Globex", merged);
        Assert.Contains("EMAIL;TYPE=WORK:jane@globex.test", merged);
        Assert.DoesNotContain("jane@acme.test", merged);      // old email replaced
        Assert.DoesNotContain("ORG:Acme", merged);

        // Everything the form doesn't model is preserved verbatim.
        Assert.Contains("PHOTO;ENCODING=b;TYPE=JPEG:/9j/EMBEDDEDBASE64DATA==", merged);
        Assert.Contains("X-CUSTOM-FIELD:keep me", merged);
        Assert.Contains("UID:abc-123", merged);
        Assert.Contains("TITLE:Engineer", merged);            // unchanged modelled field still present
        Assert.Contains("NOTE:Met at a conference", merged);

        // Re-reading the merged card is stable.
        var reread = composer.Read(merged);
        Assert.Equal("Jane Smith", reread.FormattedName);
        Assert.Single(reread.Emails);
    }

    [Fact]
    public void Merge_onto_a_blank_card_builds_a_valid_vcard()
    {
        var card = ContactCard.Empty with { FormattedName = "New Person", GivenName = "New", FamilyName = "Person" };

        var blob = composer.Merge(null, card, "new-uid");

        Assert.StartsWith("BEGIN:VCARD", blob);
        Assert.Contains("VERSION:3.0", blob);
        Assert.Contains("UID:new-uid", blob);
        Assert.Contains("FN:New Person", blob);
        Assert.EndsWith("END:VCARD\r\n", blob);
    }
}
