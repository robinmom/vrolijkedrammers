using Drammers.Infrastructure.Email;

namespace Drammers.UnitTests.Email;

/// <summary>Fase 7: afzender op het eigen maildomein zodra dat in ACS is gekoppeld.</summary>
public class EmailSenderAddressTests
{
    private static readonly EmailOptions Azure = new() { SenderDomain = "abc.azurecomm.net" };

    private static readonly EmailOptions Own = new()
    {
        SenderDomain = "abc.azurecomm.net",
        CustomSenderDomain = "vrolijkedrammers.nl",
        CustomSenders = "secretaris,optocht,penningmeester,voorzitter",
    };

    private static EmailMessage Message(string? replyTo = null, string? from = null) => new("lid@example.com", "Onderwerp", "Tekst", "<p>Tekst</p>", replyTo, from);

    [Fact]
    public void Zonder_eigen_domein_altijd_DoNotReply_van_Azure() =>
        Assert.Equal("DoNotReply@abc.azurecomm.net", EmailSenderAddress.For(Message("secretaris@vrolijkedrammers.nl", "secretaris"), Azure));

    [Theory]
    [InlineData(null, "secretaris", "secretaris@vrolijkedrammers.nl")]
    [InlineData("penningmeester@vrolijkedrammers.nl", null, "penningmeester@vrolijkedrammers.nl")]
    [InlineData("Optocht@VrolijkeDrammers.nl", null, "optocht@vrolijkedrammers.nl")]
    [InlineData("bezoeker@example.com", null, "DoNotReply@vrolijkedrammers.nl")]
    [InlineData("info@vrolijkedrammers.nl", null, "DoNotReply@vrolijkedrammers.nl")]
    [InlineData(null, "onbekend", "DoNotReply@vrolijkedrammers.nl")]
    [InlineData(null, null, "DoNotReply@vrolijkedrammers.nl")]
    public void Met_eigen_domein_de_bekende_afzender_of_DoNotReply(string? replyTo, string? from, string expected) =>
        Assert.Equal(expected, EmailSenderAddress.For(Message(replyTo, from), Own));
}
