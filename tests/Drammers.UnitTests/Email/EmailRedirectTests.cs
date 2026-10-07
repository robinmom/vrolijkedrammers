using Drammers.Infrastructure.Email;

namespace Drammers.UnitTests.Email;

/// <summary>Dev stuurt alle e-mail naar één testadres (Dev heeft een kopie van de echte leden).</summary>
public class EmailRedirectTests
{
    private static readonly EmailMessage Message = new(
        "piet@example.com", "Welkom", "Beste Piet,", "<p>Beste Piet,</p>", ReplyTo: "secretaris@vrolijkedrammers.nl",
        Attachments: [new EmailFile("factuur.pdf", "application/pdf", [1, 2, 3])]);

    [Fact]
    public void Omleiden_naar_het_testadres_met_de_oorspronkelijke_ontvanger_erbij()
    {
        var redirected = EmailRedirect.Apply(Message, "app@vrolijkedrammers.nl");

        Assert.Equal("app@vrolijkedrammers.nl", redirected.To);
        Assert.Equal("[Dev → piet@example.com] Welkom", redirected.Subject);
        Assert.StartsWith("Omgeleid vanuit Dev: deze e-mail was bedoeld voor piet@example.com.", redirected.PlainText, StringComparison.Ordinal);
        Assert.EndsWith("Beste Piet,", redirected.PlainText, StringComparison.Ordinal);
        Assert.Contains("bedoeld voor piet@example.com", redirected.Html, StringComparison.Ordinal);
        Assert.EndsWith("<p>Beste Piet,</p>", redirected.Html, StringComparison.Ordinal);
        Assert.Equal((Message.ReplyTo, Message.Attachments), (redirected.ReplyTo, redirected.Attachments));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Zonder_omleiding_blijft_alles_gelijk(string? redirectTo) =>
        Assert.Same(Message, EmailRedirect.Apply(Message, redirectTo));
}
