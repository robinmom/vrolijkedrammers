using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.IntegrationTests.Infrastructure;

namespace Drammers.IntegrationTests;

/// <summary>Fase 21i: het contactformulier vervangt de e-mailadressen op de website.</summary>
[Collection(SqlServerCollection.Name)]
public class ContactFormTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _guest = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _guest = _api.CreateClient();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Bericht_versturen_zonder_dat_adressen_op_de_site_staan()
    {
        var config = await _guest.GetFromJsonAsync<JsonElement>("/api/v1/contact");
        Assert.Equal(["secretariaat", "optocht", "penningmeester"], config.GetProperty("recipients").EnumerateArray().Select(r => r.GetProperty("key").GetString()));
        Assert.DoesNotContain("@", config.GetRawText());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("turnstileSiteKey").ValueKind);

        var response = await _guest.PostAsJsonAsync("/api/v1/contact", new
        {
            recipient = "optocht",
            name = "Jan de Bouwer",
            email = "jan@example.com",
            phone = "0612345678",
            message = "Mag onze wagen 4 meter hoog zijn?",
            website = (string?)null,
            elapsedMs = 12000,
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var mail = Assert.Single(_api.Emails.Sent, m => m.To == "optocht@vrolijkedrammers.nl");
        Assert.Equal("jan@example.com", mail.ReplyTo);
        Assert.StartsWith("Contactformulier (Optocht): Jan de Bouwer", mail.Subject);

        var invalid = await _guest.PostAsJsonAsync("/api/v1/contact", new { recipient = "optocht", name = "Jan", email = "geen-adres", message = "Hoi", elapsedMs = 9000 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        foreach (var path in new[] { "/contact", "/optocht", "/" })
        {
            var html = await _guest.GetStringAsync(path);
            Assert.DoesNotContain("@vrolijkedrammers.nl", html);
            Assert.Contains("/contact?aan=optocht#formulier", html);
        }

        var contact = await _guest.GetStringAsync("/contact");
        Assert.Contains("id=\"contact\"", contact);
        Assert.Contains("js/forms/contact.js", contact);
    }
}
