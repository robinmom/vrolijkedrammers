using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Members;

namespace Drammers.UnitTests.Email;

/// <summary>
/// De verificatiecode van een aanmelding gaat via de mailbox app@ in Microsoft 365, niet via ACS. ACS knijpt af op
/// 100 mails per uur per abonnement, en een mailing naar de leden deelt die pot: op 11-11-2026 lag het aanmeldformulier
/// daardoor plat zodra er 135 uitnodigingen de deur uit gingen. Een aanmelding mag nooit achter een mailing aansluiten.
/// </summary>
public class AanmeldcodeRouteTests
{
    private static readonly EmailOptions Options = new()
    {
        SenderDomain = "abc.azurecomm.net",
        CustomSenderDomain = "vrolijkedrammers.nl",
        CustomSenders = "app,secretaris,optocht,penningmeester,voorzitter,dansgardefestival",
        MailboxSenders = "voorzitter,secretaris,dansgardefestival,app",
    };

    private static EmailMessage Code() => MembershipApplications.CodeMail("nieuw.lid@example.com", "Dieke", "123456");

    [Fact]
    public void De_code_gaat_via_de_mailbox_en_niet_via_ACS() =>
        Assert.Equal("app@vrolijkedrammers.nl", MailboxEmailSender.Mailbox(Code(), Options));

    /// <summary>Kan Graph de mailbox niet aan, dan valt ACS terug op hetzelfde adres — niet op DoNotReply.</summary>
    [Fact]
    public void De_terugval_via_ACS_houdt_dezelfde_afzender() =>
        Assert.Equal("app@vrolijkedrammers.nl", EmailSenderAddress.For(Code(), Options));

    /// <summary>
    /// En die terugval moet ook echt gebeuren. Op 12-11-2026 gaf Exchange Online uren achtereen 500 en 504; de code bleef
    /// toen steken en de bezoeker kreeg een foutmelding, terwijl ACS gewoon werkte. Een mailing valt juist niet terug:
    /// die biedt de outbox later opnieuw aan en hoort niet uit de ACS-pot te komen.
    /// </summary>
    [Fact]
    public void De_code_wacht_niet_op_een_haperende_mailbox()
    {
        Assert.True(Code().MailboxOptional);
        Assert.False(MemberSplits.InvitationMail("lid@example.com", "Piet", "Marie", "https://dvd.test/lid-worden/?splitsen=abc").MailboxOptional);
    }

    /// <summary>
    /// Staat app@ nog niet in <see cref="EmailOptions.MailboxSenders"/>, dan gaat de code gewoon via ACS. Dat is de
    /// toestand vlak na deze wijziging en vóór het zetten van DVD_EMAIL_MAILBOX_SENDERS: werkend, maar zonder de
    /// bescherming tegen de limiet.
    /// </summary>
    [Fact]
    public void Zonder_de_instelling_blijft_de_code_werken_via_ACS()
    {
        var zonder = new EmailOptions
        {
            SenderDomain = Options.SenderDomain,
            CustomSenderDomain = Options.CustomSenderDomain,
            CustomSenders = Options.CustomSenders,
            MailboxSenders = "voorzitter,secretaris",
        };

        Assert.Null(MailboxEmailSender.Mailbox(Code(), zonder));
        Assert.Equal("app@vrolijkedrammers.nl", EmailSenderAddress.For(Code(), zonder));
    }

    /// <summary>Een mailing naar de leden mag niet per ongeluk dezelfde afzender krijgen: die hoort bij het secretariaat.</summary>
    [Fact]
    public void De_splitsuitnodiging_blijft_van_het_secretariaat() =>
        Assert.Equal("secretaris@vrolijkedrammers.nl",
            EmailSenderAddress.For(MemberSplits.InvitationMail("lid@example.com", "Piet", "Marie", "https://dvd.test/lid-worden/?splitsen=abc"), Options));

    /// <summary>
    /// En de splitsuitnodiging gaat sinds 11-11-2026 óók via de mailbox, dus niet meer uit de ACS-pot. Dat was de
    /// tweede helft van de storing: 134 uitnodigingen legden de pot van 100 per uur leeg.
    /// </summary>
    [Fact]
    public void De_splitsuitnodiging_gaat_via_de_mailbox_en_niet_via_ACS() =>
        Assert.Equal("secretaris@vrolijkedrammers.nl", MailboxEmailSender.Mailbox(
            MemberSplits.InvitationMail("lid@example.com", "Piet", "Marie", "https://dvd.test/lid-worden/?splitsen=abc"), Options));
}
