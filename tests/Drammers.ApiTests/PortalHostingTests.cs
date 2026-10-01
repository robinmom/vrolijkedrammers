using System.Net;
using Drammers.ApiTests.Infrastructure;

namespace Drammers.ApiTests;

/// <summary>Het beheerportal wordt vanuit de API-app geserveerd onder /beheer (OQ-76: alles in de EU).</summary>
public class PortalHostingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new() { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/beheer/")]
    [InlineData("/beheer/leden/123")]
    public async Task Portal_en_client_side_routes_geven_index_html_met_CSP(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' https://*.ciamlogin.com", csp, StringComparison.Ordinal);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task Beheer_zonder_slash_wordt_doorgestuurd()
    {
        var response = await _client.GetAsync("/beheer");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/beheer/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Ontbrekend_portalbestand_geeft_404_en_geen_index_html()
    {
        var response = await _client.GetAsync("/beheer/assets/bestaat-niet.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Onbekende_API_route_valt_niet_terug_op_het_portal()
    {
        var response = await _client.GetAsync("/api/v1/bestaat-niet");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }
}
