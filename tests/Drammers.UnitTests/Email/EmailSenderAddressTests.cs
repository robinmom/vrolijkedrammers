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
    [InlineData("penningmeester@vrolijkedrammers.nl", "penningmeester", "penningmeester@vrolijkedrammers.nl")]
    [InlineData("bezoeker@example.com", "Optocht", "optocht@vrolijkedrammers.nl")]
    [InlineData("bezoeker@example.com", null, "DoNotReply@vrolijkedrammers.nl")]
    [InlineData("info@vrolijkedrammers.nl", null, "DoNotReply@vrolijkedrammers.nl")]
    [InlineData(null, "onbekend", "DoNotReply@vrolijkedrammers.nl")]
    [InlineData(null, null, "DoNotReply@vrolijkedrammers.nl")]
    public void Met_eigen_domein_de_bekende_afzender_of_DoNotReply(string? replyTo, string? from, string expected) =>
        Assert.Equal(expected, EmailSenderAddress.For(Message(replyTo, from), Own));

    /// <summary>M1: het antwoordadres (bij het contactformulier door een bezoeker ingevuld) bepaalt nooit de afzender.</summary>
    [Theory]
    [InlineData("voorzitter@vrolijkedrammers.nl")]
    [InlineData("Penningmeester@VrolijkeDrammers.nl")]
    [InlineData("secretaris@vrolijkedrammers.nl")]
    public void Antwoordadres_op_eigen_domein_maakt_geen_eigen_afzender(string replyTo) =>
        Assert.Equal("DoNotReply@vrolijkedrammers.nl", EmailSenderAddress.For(Message(replyTo), Own));

    [Theory]
    [InlineData("voorzitter@vrolijkedrammers.nl", true)]
    [InlineData(" Iemand@VrolijkeDrammers.NL ", true)]
    [InlineData("jan@example.com", false)]
    [InlineData("jan@vrolijkedrammers.nl.example.com", false)]
    [InlineData("geen-adres", false)]
    [InlineData(null, false)]
    public void Eigen_domein_herkennen(string? address, bool expected)
    {
        Assert.Equal(expected, EmailSenderAddress.IsOwnDomain(address, Own));
        Assert.False(EmailSenderAddress.IsOwnDomain(address, Azure));
    }
}
