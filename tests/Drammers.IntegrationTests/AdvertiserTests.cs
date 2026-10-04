using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.Website;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 27b: het advertentie-overzicht inlezen (nagemaakte gegevens), beheren en de campagne volgen.</summary>
[Collection(SqlServerCollection.Name)]
public class AdvertiserTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> KaderlidAsync(string fullName)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var member = new Member { Id = IdGenerator.NewId(), MemberNumber = $"9{Random.Shared.Next(1000, 9999)}", FullName = fullName, MembershipStatus = MembershipStatus.Active };
        db.Members.Add(member);
        var committee = await db.Committees.OrderBy(c => c.Id).FirstAsync();
        db.CommitteeMembers.Add(new CommitteeMember { Id = IdGenerator.NewId(), CommitteeId = committee.Id, MemberId = member.Id, Name = fullName });
        await db.SaveChangesAsync();
        return member.Id;
    }

    /// <summary>Zoals "Advertentie overzicht": tabblad "Campagne 2022", kolomkoppen met regeleinden.</summary>
    private static MultipartFormDataContent Workbook(params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Campagne 2022");
        string[] headers =
        [
            "NAAM COLLECTANT", "NR.", "PAGINA", "NAAM BEDRIJF", "CONTACT-PERSOON", "TELEFOON ALGEMEEN", "MOBIEL NUMMER", "MAILADRES", "ADRES", "POST-CODE",
            "PLAATS", "A/V/G", "IBAN ", "M/C/R/B", "BIJZONDERHEDEN", "SITE", "SEPA MACHTIGINGS-NUM:", "BIJDRAGE\n 2025", "BIJDRAGE 2026", "OPMERKING", "CHECK",
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                sheet.Cell(r + 2, c + 1).Value = rows[r][c] switch
                {
                    null => Blank.Value,
                    int i => i,
                    decimal d => d,
                    var o => o.ToString(),
                };
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(stream.ToArray());
        file.Headers.ContentType = new("application/vnd.ms-excel.sheet.macroEnabled.12");
        content.Add(file, "file", "Advertentie overzicht 2026.xlsm");
        return content;
    }

    private static object?[] Row(string collector, int number, string company, string kind, string payment, string? iban, string? mandate, object? y2025, object? y2026) =>
        [collector, number, 4, company, "Jan Test", "0314-000000", null, $"info{number}@example.com", "Dorpsstraat 1", "6941 XX", "Loil", kind, iban, payment,
            null, "www.example.com", mandate, y2025, y2026, number == 2 ? "Liever na 18 uur langskomen" : null, null];

    [Fact]
    public async Task Overzicht_inlezen_controleren_en_de_campagne_volgen()
    {
        var alfred = await KaderlidAsync("Alfred Voorbeeld");

        // Eerst met een R in M/C/R/B: de import meldt de regel en slaat niets op.
        var wrong = Workbook(
            Row("ALFRED VOORBEELD", 1, "Bakkerij De Test", "A", "M", "NL91 ABNA 0417 1643 00", "DVD000000001", 35, 35),
            Row("Onbekende Collectant", 2, "Garage Proef", "A", "R", null, null, 70, "GRATIS"));
        var preview = await (await _bestuur.PostAsync("/api/v1/admin/advertisers/import/preview", wrong)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, preview.GetProperty("rows").GetInt32());
        var error = Assert.Single(preview.GetProperty("errors").EnumerateArray());
        Assert.Equal(3, error.GetProperty("row").GetInt32());
        Assert.Contains("M (machtiging) of C (contant)", error.GetProperty("message").GetString());
        Assert.Equal(["Onbekende Collectant"], preview.GetProperty("unknownCollectors").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PostAsync("/api/v1/admin/advertisers/import", wrong)).StatusCode);

        // Aangepast: nu wordt alles ingelezen; de collectant is herkend op naam (hoofdletters maken niet uit).
        var fixedFile = Workbook(
            Row("ALFRED VOORBEELD", 1, "Bakkerij De Test", "A", "M", "NL91 ABNA 0417 1643 00", "DVD000000001", 35, 35),
            Row("Onbekende Collectant", 2, "Garage Proef", "G", "C", null, null, 70, "GRATIS"),
            Row("Alfred Voorbeeld", 3, "Kapsalon Stop", "A", "M", null, "DVD000000003", 50, 0));
        var imported = await (await _bestuur.PostAsync("/api/v1/admin/advertisers/import", fixedFile)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((3, 0), (imported.GetProperty("new").GetInt32(), imported.GetProperty("updated").GetInt32()));
        Assert.Contains(imported.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("message").GetString()!.Contains("machtiging zonder IBAN", StringComparison.Ordinal));

        var list = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/advertisers");
        var bakkerij = list!.Single(a => a.GetProperty("number").GetInt32() == 1);
        Assert.Equal(("Alfred Voorbeeld", true, 2026, 35m), (bakkerij.GetProperty("collectorName").GetString(), bakkerij.GetProperty("hasIban").GetBoolean(),
            bakkerij.GetProperty("lastYear").GetInt32(), bakkerij.GetProperty("lastAmount").GetDecimal()));
        var garage = list!.Single(a => a.GetProperty("number").GetInt32() == 2);
        Assert.Equal(("Gift", "Cash", "Onbekende Collectant"), (garage.GetProperty("kind").GetString(), garage.GetProperty("payment").GetString(),
            garage.GetProperty("importedCollectorName").GetString()));

        // IBAN alleen gemaskeerd, voluit via een apart verzoek (gelogd).
        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/advertisers/{bakkerij.GetProperty("id").GetGuid()}");
        Assert.Equal("**** 4300", detail.GetProperty("maskedIban").GetString());
        var iban = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/advertisers/{bakkerij.GetProperty("id").GetGuid()}/iban");
        Assert.Equal("NL91ABNA0417164300", iban.GetProperty("iban").GetString());

        // Campagne 2027: alles open, met het bedrag van vorig jaar als verwachting.
        var status = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/advertisers/status?year=2027");
        var totals = status.GetProperty("totals");
        Assert.Equal((3, 0, 3), (totals.GetProperty("total").GetInt32(), totals.GetProperty("collected").GetInt32(), totals.GetProperty("open").GetInt32()));
        Assert.Equal(85m, totals.GetProperty("expectedAmount").GetDecimal());

        // Opgehaald zonder bedrag = bedrag van vorig jaar; filter op collectant.
        var bakkerijId = bakkerij.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/advertisers/{bakkerijId}/years/2027",
            new { status = "Collected", amount = (decimal?)null, note = (string?)null })).StatusCode);
        var mine = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/advertisers/status?year=2027&collector={alfred}");
        Assert.Equal(2, mine.GetProperty("rows").GetArrayLength());
        Assert.Equal((1, 35m), (mine.GetProperty("totals").GetProperty("collected").GetInt32(), mine.GetProperty("totals").GetProperty("collectedAmount").GetDecimal()));
        var perCollector = status.GetProperty("perCollector").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Alfred Voorbeeld", "Onbekende Collectant"], perCollector);

        // Opnieuw inlezen werkt bij maar laat de stand uit het portal staan.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/advertisers/{bakkerijId}/years/2026",
            new { status = "Stopped", amount = (decimal?)null, note = "Toch niet" })).StatusCode);
        var again = await (await _bestuur.PostAsync("/api/v1/admin/advertisers/import", fixedFile)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((0, 3), (again.GetProperty("new").GetInt32(), again.GetProperty("updated").GetInt32()));
        var after = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/advertisers/{bakkerijId}");
        Assert.Equal("Stopped", after.GetProperty("years").EnumerateArray().Single(y => y.GetProperty("year").GetInt32() == 2026).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Adverteerder_toevoegen_met_collectant_uit_het_kader_en_alleen_met_het_recht()
    {
        var kader = await KaderlidAsync("Karin Kader");
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            db.Members.Add(new Member { Id = IdGenerator.NewId(), MemberNumber = "8001", FullName = "Gewoon Lid", MembershipStatus = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }

        var collectors = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/advertisers/collectors");
        Assert.Equal(["Karin Kader"], collectors!.Select(c => c.GetProperty("name").GetString()));

        object Body(Guid? collector, string iban = "NL91ABNA0417164300") => new
        {
            number = 10,
            companyName = "Nieuwe Zaak",
            contactName = (string?)null,
            phone = (string?)null,
            mobile = (string?)null,
            email = "zaak@example.com",
            addressLine = (string?)null,
            postalCode = (string?)null,
            city = "Loil",
            website = (string?)null,
            page = (string?)null,
            kind = "Advertisement",
            payment = "Mandate",
            iban,
            mandateReference = "DVD-ADV-10",
            collectorMemberId = collector,
            notes = (string?)null,
            active = true,
        };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PostAsJsonAsync("/api/v1/admin/advertisers", Body(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PostAsJsonAsync("/api/v1/admin/advertisers", Body(kader, "NL00 FOUT 1234"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _bestuur.PostAsJsonAsync("/api/v1/admin/advertisers", Body(kader))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PostAsJsonAsync("/api/v1/admin/advertisers", Body(kader))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync("/api/v1/admin/advertisers/campaign-year", new { year = 2028 })).StatusCode);
        Assert.Equal(2028, (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/advertisers/status")).GetProperty("year").GetInt32());

        var lid = _api.ClientFor((await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/advertisers")).StatusCode);
    }
}
