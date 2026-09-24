using System.Net;
using System.Text.Json;
using Drammers.ApiTests.Infrastructure;

namespace Drammers.ApiTests;

public class FoundationEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_live_geeft_200()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Onbekende_route_geeft_ProblemDetails_met_code_en_traceId()
    {
        var response = await _client.GetAsync("/bestaat-niet");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NOT_FOUND", body.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Onverwachte_fout_geeft_generieke_500_zonder_interne_details()
    {
        var response = await _client.GetAsync("/test/throw");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Geheime interne details", content, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", content, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(content);
        Assert.Equal("UNEXPECTED_ERROR", body.RootElement.GetProperty("code").GetString());
    }
}
