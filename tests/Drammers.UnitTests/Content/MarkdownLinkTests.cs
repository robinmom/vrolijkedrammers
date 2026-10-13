using Drammers.Infrastructure.Content;

namespace Drammers.UnitTests.Content;

public class MarkdownLinkTests
{
    [Theory]
    [InlineData("[foto's](/fotos/abc)", "<a href=\"/fotos/abc\">")]
    [InlineData("[optocht](https://www.vrolijkedrammers.nl/optocht)", "<a href=\"https://www.vrolijkedrammers.nl/optocht\">")]
    [InlineData("[mail](mailto:secretaris@vrolijkedrammers.nl)", "<a href=\"mailto:secretaris@vrolijkedrammers.nl\">")]
    public void Links_naar_de_eigen_site_openen_in_hetzelfde_venster(string markdown, string expected) =>
        Assert.Contains(expected, MarkdownRenderer.ToSafeHtml(markdown), StringComparison.Ordinal);

    [Fact]
    public void Andere_sites_openen_in_een_nieuw_tabblad()
    {
        var html = MarkdownRenderer.ToSafeHtml("[krant](https://example.com/krant)")!;
        Assert.Contains("target=\"_blank\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html, StringComparison.Ordinal);
        Assert.False(MarkdownRenderer.IsOwnSite("https://vrolijkedrammers.nl.example.com/x"));
        Assert.False(MarkdownRenderer.IsOwnSite("//example.com/x"));
    }
}
