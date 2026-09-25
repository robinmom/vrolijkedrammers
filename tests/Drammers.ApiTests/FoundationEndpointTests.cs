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
    public async Task Health_ready_geeft_200_zonder_geconfigureerde_Azure_resources()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_zonder_Entra_configuratie_weigert_elk_token()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new("Bearer", TestTokens.Create(TestTokens.DevAudience, "dev"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_zonder_annotatie_is_afgeschermd()
    {
        var response = await _client.GetAsync("/test/unannotated");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
