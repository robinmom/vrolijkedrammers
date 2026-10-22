using System.Net;
using System.Net.Http.Json;
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
        Assert.False(string.IsNullOrEmpty(Assert.Single(response.Headers.GetValues("X-App-Version"))));
    }

    [Fact]
    public async Task Health_ready_geeft_200_zonder_geconfigureerde_Azure_resources()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Portal_config_geeft_de_omgeving_mee()
    {
        // Zonder vereiste environmentAccess is dit productie: de portal kleurt de achtergrond niet.
        var prod = await _client.GetFromJsonAsync<JsonElement>("/api/v1/portal-config");
        Assert.Equal("prod", prod.GetProperty("environment").GetString());

        // Met environmentAccess 'dev' kleurt de portal de achtergrond zodat je ziet dat je niet in productie werkt.
        using var devClient = factory.WithWebHostBuilder(builder => builder.UseSetting("Auth:RequiredEnvironmentAccess", "dev")).CreateClient();
        var dev = await devClient.GetFromJsonAsync<JsonElement>("/api/v1/portal-config");
        Assert.Equal("dev", dev.GetProperty("environment").GetString());
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
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHORIZED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Onbekende_route_geeft_ProblemDetails_met_code_en_traceId()
    {
        // Onder de hoofdmap staat sinds fase 21c de website (met een 404-pagina in HTML); de API blijft ProblemDetails geven.
        var response = await _client.GetAsync("/api/v1/bestaat-niet");

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

    [Fact]
    public async Task Ook_een_500_heeft_de_security_headers_en_HSTS_van_een_jaar()
    {
        // De exception handler wist eerder gezette headers; ze worden daarom pas bij het starten van de response gezet.
        var https = factory.CreateClient(new() { BaseAddress = new Uri("https://dvd.test") });

        var response = await https.GetAsync("/test/throw");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("max-age=31536000; includeSubDomains", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task Geen_HSTS_over_http()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }
}
