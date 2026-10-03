using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 23c: SEPA-incasso van de contributie (pain.008.001.08).</summary>
[Collection(SqlServerCollection.Name)]
public class SepaCollectionTests(SqlServerFixture sql) : IAsyncLifetime
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.08";

    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task MemberAsync(string number, string name, string? iban, string? mandate = null, DateOnly? signed = null, DateOnly? birth = null)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<MemberIbanProtector>();
        db.Members.Add(new Member
        {
            Id = IdGenerator.NewId(),
            MemberNumber = number,
            FullName = name,
            LastName = name,
            MembershipStatus = MembershipStatus.Active,
            MembershipKind = MembershipKind.OnePerson,
            BirthDate = birth ?? new DateOnly(1980, 1, 1),
            IbanProtected = iban is null ? null : protector.Protect(iban),
            IbanLast4 = iban?[^4..],
            MandateReference = iban is null ? null : mandate ?? $"M-{number}",
            MandateSignedOn = signed,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Incassobestand_met_voorbeeld_en_run()
    {
        await MemberAsync("1", "Piet Bergé", "NL91ABNA0417164300", signed: new DateOnly(2015, 3, 1));
        await MemberAsync("2", "Oma Jansen", "NL02RABO0123456789", birth: new DateOnly(1950, 1, 1));
        await MemberAsync("3", "Nieuw Lid", "NL20INGB0001234567", mandate: "DVD-3-20261003", signed: new DateOnly(2026, 10, 3));
        await MemberAsync("4", "Zonder Rekening", null);

        // Zonder gegevens van de vereniging kan het niet.
        var date = "2027-03-01";
        var early = await _bestuur.PostAsJsonAsync("/api/v1/admin/collections", new { date, description = (string?)null });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, early.StatusCode);
        var badId = await _bestuur.PutAsJsonAsync("/api/v1/admin/collections/creditor", new { name = "CV De Vrolijke Drammers", iban = "NL91ABNA0417164300", creditorId = "onzin" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badId.StatusCode);
        var creditor = await _bestuur.PutAsJsonAsync("/api/v1/admin/collections/creditor",
            new { name = "CV De Vrolijke Drammers", iban = "NL39 RABO 0300 0652 64", creditorId = "nl12zzz123456780000" });
        Assert.Equal(HttpStatusCode.NoContent, creditor.StatusCode);

        var preview = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/collections/preview?date={date}");
        Assert.Equal((3, 32.50m + 22.00m + 32.50m), (preview.GetProperty("count").GetInt32(), preview.GetProperty("total").GetDecimal()));
        Assert.Equal(["Geen IBAN of machtiging"], preview.GetProperty("skipped").EnumerateArray().Select(s => s.GetProperty("reason").GetString()));
        var lines = preview.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("memberNumber").GetString()!);
        Assert.Equal(("Rcur", "Frst"), (lines["1"].GetProperty("sequenceType").GetString(), lines["3"].GetProperty("sequenceType").GetString()));
        Assert.Contains("onbekend", lines["2"].GetProperty("warning").GetString());

        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/collections", new { date, description = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var runId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var file = await _bestuur.GetAsync($"/api/v1/admin/collections/{runId}/file");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        var xml = XDocument.Parse(await file.Content.ReadAsStringAsync());
        var header = xml.Root!.Element(Ns + "CstmrDrctDbtInitn")!.Element(Ns + "GrpHdr")!;
        Assert.Equal(("3", "87.00"), (header.Element(Ns + "NbOfTxs")!.Value, header.Element(Ns + "CtrlSum")!.Value));
        var payments = xml.Descendants(Ns + "PmtInf").ToList();
        Assert.Equal(["FRST", "RCUR"], payments.Select(p => p.Descendants(Ns + "SeqTp").Single().Value));
        Assert.All(payments, p => Assert.Equal("NL12ZZZ123456780000", p.Element(Ns + "CdtrSchmeId")!.Descendants(Ns + "Id").Last().Value));
        Assert.Equal("NL39RABO0300065264", payments[0].Element(Ns + "CdtrAcct")!.Descendants(Ns + "IBAN").Single().Value);
        var transactions = xml.Descendants(Ns + "DrctDbtTxInf").ToDictionary(t => t.Descendants(Ns + "EndToEndId").Single().Value);
        var piet = transactions["1-20270301"];
        Assert.Equal(("32.50", "NL91ABNA0417164300", "Piet Berge", "2015-03-01"), (piet.Element(Ns + "InstdAmt")!.Value,
            piet.Descendants(Ns + "IBAN").Single().Value, piet.Element(Ns + "Dbtr")!.Element(Ns + "Nm")!.Value, piet.Descendants(Ns + "DtOfSgntr").Single().Value));
        Assert.Equal("2009-11-01", transactions["2-20270301"].Descendants(Ns + "DtOfSgntr").Single().Value);
        Assert.Equal("22.00", transactions["2-20270301"].Element(Ns + "InstdAmt")!.Value);

        // Na het downloaden telt de app-machtiging als eerder geïncasseerd: volgende keer RCUR.
        var next = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/collections/preview?date=2028-03-01");
        Assert.All(next.GetProperty("lines").EnumerateArray(), l => Assert.Equal("Rcur", l.GetProperty("sequenceType").GetString()));

        var runs = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/collections");
        Assert.NotEqual(JsonValueKind.Null, runs[0].GetProperty("exportedAt").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.DeleteAsync($"/api/v1/admin/collections/{runId}")).StatusCode);

        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/collections")).StatusCode);
    }
}
