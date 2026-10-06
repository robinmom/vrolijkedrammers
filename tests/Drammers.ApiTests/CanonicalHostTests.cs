using System.Net;
using Drammers.ApiTests.Infrastructure;

namespace Drammers.ApiTests;

/// <summary>Fase 7: het kale domein stuurt door naar www; het azurewebsites-adres blijft werken.</summary>
public class CanonicalHostTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client() => factory.WithWebHostBuilder(b => b.UseSetting("Website:CanonicalHost", "www.vrolijkedrammers.nl"))
        .CreateClient(new() { AllowAutoRedirect = false });

    [Fact]
    public async Task Kale_domein_gaat_met_pad_en_query_naar_www()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nieuws?pagina=2");
        request.Headers.Host = "vrolijkedrammers.nl";

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://www.vrolijkedrammers.nl/nieuws?pagina=2", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Post_naar_het_kale_domein_houdt_methode_en_body_via_308()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/contact");
        request.Headers.Host = "vrolijkedrammers.nl";

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.Equal("https://www.vrolijkedrammers.nl/api/v1/contact", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("www.vrolijkedrammers.nl")]
    [InlineData("app-dvd-api-prod.azurewebsites.net")]
    public async Task Hoofdadres_en_azurewebsites_worden_niet_doorgestuurd(string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Host = host;

        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Buiten_productie_geen_zoekmachines()
    {
        var client = factory.CreateClient();

        var robots = await client.GetAsync("/robots.txt");

        Assert.Equal("User-agent: *\nDisallow: /\n", await robots.Content.ReadAsStringAsync());
        Assert.Equal("noindex, nofollow", Assert.Single(robots.Headers.GetValues("X-Robots-Tag")));
    }
}
