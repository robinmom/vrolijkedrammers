using Drammers.Infrastructure.Identity.Entra;

namespace Drammers.UnitTests.Identity;

/// <summary>Welk e-mailadres de API aan een inlog koppelt (ADR-014, herzien 2026-09-27).</summary>
public class GraphIdentityTests
{
    private const string Domain = "vrolijkedrammersapp.onmicrosoft.com";

    [Fact]
    public void Inlog_met_e_mail_en_code_zoals_Entra_die_opslaat()
    {
        // Letterlijk zoals Graph het gaf voor een zelf gemaakte inlog (2026-09-27).
        var identities = new[]
        {
            ("federated", "mail", "robinmom@outlook.com"),
            ("userPrincipalName", Domain, "f04ee3a0-01a9-44f3-90c9-a8a3799cabe4@vrolijkedrammersapp.onmicrosoft.com"),
        };

        Assert.Equal("robinmom@outlook.com", GraphEntraUserDirectory.SignInEmail(identities, Domain));
    }

    [Fact]
    public void Lokaal_account_met_wachtwoord()
    {
        var identities = new[] { ("emailAddress", Domain, "robin.mom@vrolijkedrammers.nl"), ("userPrincipalName", Domain, "x@" + Domain) };

        Assert.Equal("robin.mom@vrolijkedrammers.nl", GraphEntraUserDirectory.SignInEmail(identities, Domain));
    }

    [Fact]
    public void Andere_federatie_of_alleen_een_UPN_telt_niet()
    {
        var identities = new[] { ("federated", "google.com", "iemand@gmail.com"), ("userPrincipalName", Domain, "y@" + Domain) };

        Assert.Null(GraphEntraUserDirectory.SignInEmail(identities, Domain));
    }
}
