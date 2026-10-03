using Drammers.Infrastructure.Email;

namespace Drammers.UnitTests.Email;

/// <summary>Het logo van de vereniging bovenaan elke e-mail.</summary>
public class EmailBrandingTests
{
    private const string Logo = "https://app.example/_content/Drammers.Website/img/logo.png";

    [Fact]
    public void Logo_bovenaan_een_keer_en_alleen_met_een_url()
    {
        var html = EmailBranding.WithLogo("<p>Beste Piet,</p>", Logo);
        Assert.StartsWith("<div style=\"text-align:center", html, StringComparison.Ordinal);
        Assert.Contains($"src=\"{Logo}\"", html, StringComparison.Ordinal);
        Assert.EndsWith("<p>Beste Piet,</p>", html, StringComparison.Ordinal);
        Assert.Equal(html, EmailBranding.WithLogo(html, Logo));
        Assert.Equal("<p>Hoi</p>", EmailBranding.WithLogo("<p>Hoi</p>", null));
    }
}
