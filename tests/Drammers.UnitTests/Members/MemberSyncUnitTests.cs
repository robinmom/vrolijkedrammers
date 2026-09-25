using System.Net;
using System.Text;
using Drammers.Infrastructure.EBoekhouden;
using Drammers.Modules.Import.Sync;
using Drammers.Modules.Membership.Members;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Drammers.UnitTests.Members;

public class DutchNameParserTests
{
    [Theory]
    [InlineData("Piet van der Berg", "Piet", "van der", "Berg")]
    [InlineData("Berg, Piet van der", "Piet", "van der", "Berg")]
    [InlineData("P.J. de Vries", "P.J.", "de", "Vries")]
    [InlineData("P. J. de Vries", "P. J.", "de", "Vries")]
    [InlineData("Anna Jansen", "Anna", null, "Jansen")]
    [InlineData("  Anna   Maria  ", "Anna", null, "Maria")]
    [InlineData("Jan van 't Hek", "Jan", "van 't", "Hek")]
    [InlineData("Drammer", null, null, "Drammer")]
    public void Splitst_de_naam(string input, string? first, string? prefix, string? last)
    {
        var name = DutchNameParser.Parse(input);
        Assert.Equal((first, prefix, last), (name.FirstName, name.NamePrefix, name.LastName));
    }

    [Fact]
    public void Lege_naam_geeft_lege_delen() => Assert.Equal(new ParsedName(null, null, null), DutchNameParser.Parse("  "));
}

public class MemberFieldMappingTests
{
    [Fact]
    public void Standaard_is_niets_gemapt_en_geldig()
    {
        Assert.Null(MemberFieldMapping.Default.Validate());
        Assert.Null(MemberFieldMapping.Default.BirthDate);
    }

    [Fact]
    public void Onbekend_veld_en_dubbel_gebruik_zijn_ongeldig()
    {
        Assert.NotNull((MemberFieldMapping.Default with { BirthDate = "iban" }).Validate());
        Assert.NotNull((MemberFieldMapping.Default with { BirthDate = "freeText1", JoinYear = "freeText1" }).Validate());
        Assert.Null((MemberFieldMapping.Default with { BirthDate = "freeText1", JoinYear = "freeText2" }).Validate());
    }
}

/// <summary>Contracttest van de e-Boekhouden-client tegen het gedrag uit de OpenAPI-spec (ADR-010).</summary>
public class EBoekhoudenClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Authorization, string? Body)> Requests { get; } = [];

        private int _member2Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var auth = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
            var path = request.RequestUri!.PathAndQuery;
            Requests.Add((request.Method, path, auth, body));
            return (request.Method.Method, path) switch
            {
                ("POST", "/v1/session") => Json("""{"token":"sessie-123","expiresIn":3600}"""),
                ("GET", "/v1/member?limit=500&offset=0") => Json("""{"items":[{"id":1,"memberNumber":"001"},{"id":2,"memberNumber":"002"}],"count":2}"""),
                ("GET", "/v1/member/1") => Json("""{"id":1,"memberNumber":"001","name":"Piet","emailAddress":"piet@example.com","iban":"NL91ABNA0417164300","note":"geheim","freeText1":"1980-01-01"}"""),
                ("GET", "/v1/member/2") => ++_member2Calls == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Json("""{"id":2,"memberNumber":"002","name":"Kees"}"""),
                ("DELETE", "/v1/session") => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static (IEBoekhoudenClient Client, StubHandler Handler) Create()
    {
        var handler = new StubHandler();
        var options = Options.Create(new EBoekhoudenOptions { ApiToken = "api-token", Source = "Drammers", RequestsPerSecond = 1000 });
        var client = new EBoekhoudenClient(new HttpClient(handler), options, new ServiceCollection().BuildServiceProvider(), NullLogger<EBoekhoudenClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task Sessie_lijst_detail_retry_en_afmelden()
    {
        var (client, handler) = Create();

        await using (var session = await client.OpenSessionAsync(CancellationToken.None))
        {
            var members = await session.ListMembersAsync(CancellationToken.None);
            Assert.Equal(["001", "002"], members.Select(m => m.MemberNumber));

            var piet = await session.GetMemberAsync(1, CancellationToken.None);
            Assert.Equal(("Piet", "piet@example.com", "1980-01-01"), (piet.Name, piet.EmailAddress, piet.FreeText("freeText1")));

            // 429 → opnieuw proberen
            Assert.Equal("Kees", (await session.GetMemberAsync(2, CancellationToken.None)).Name);
        }

        var login = handler.Requests[0];
        Assert.Equal((HttpMethod.Post, "/v1/session"), (login.Method, login.Path));
        Assert.Contains("\"accessToken\":\"api-token\"", login.Body);
        Assert.Contains("\"source\":\"Drammers\"", login.Body);
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal("sessie-123", r.Authorization));
        Assert.Equal(2, handler.Requests.Count(r => r.Path == "/v1/member/2"));
        Assert.Equal((HttpMethod.Delete, "/v1/session"), (handler.Requests[^1].Method, handler.Requests[^1].Path));
    }

    [Fact]
    public void Bankgegevens_en_notities_hebben_geen_plek_in_het_model()
    {
        string[] forbidden = ["Iban", "Bic", "Note", "Mandate", "MandateId", "EmailAddressInvoice", "EmailAddressReminder"];
        Assert.All(forbidden, name => Assert.Null(typeof(EbMember).GetProperty(name)));
    }

    [Fact]
    public async Task Ongeldig_token_geeft_een_duidelijke_melding()
    {
        var handler = new StubHandlerWith(HttpStatusCode.Forbidden);
        var client = new EBoekhoudenClient(new HttpClient(handler), Options.Create(new EBoekhoudenOptions { ApiToken = "fout" }),
            new ServiceCollection().BuildServiceProvider(), NullLogger<EBoekhoudenClient>.Instance);

        var ex = await Assert.ThrowsAsync<EBoekhoudenException>(() => client.OpenSessionAsync(CancellationToken.None));
        Assert.Contains("API-token", ex.Message);
    }

    [Fact]
    public async Task Zonder_token_en_zonder_Key_Vault_is_e_Boekhouden_niet_geconfigureerd()
    {
        var client = new EBoekhoudenClient(new HttpClient(new StubHandlerWith(HttpStatusCode.OK)), Options.Create(new EBoekhoudenOptions()),
            new ServiceCollection().BuildServiceProvider(), NullLogger<EBoekhoudenClient>.Instance);

        var ex = await Assert.ThrowsAsync<EBoekhoudenException>(() => client.OpenSessionAsync(CancellationToken.None));
        Assert.Contains("niet geconfigureerd", ex.Message);
    }

    private sealed class StubHandlerWith(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
