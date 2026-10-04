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

    /// <summary>Een lid met een account, en optioneel in het kader.</summary>
    private async Task<(Guid MemberId, HttpClient Client)> LidMetAccountAsync(string fullName, string email, bool kader)
    {
        var memberId = kader ? await KaderlidAsync(fullName) : Guid.Empty;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            if (!kader)
            {
                memberId = IdGenerator.NewId();
                db.Members.Add(new Member { Id = memberId, MemberNumber = "7001", FullName = fullName, MembershipStatus = MembershipStatus.Active });
                await db.SaveChangesAsync();
            }
        }

        var (userId, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.MemberId, memberId));
        }

        return (memberId, _api.ClientFor(oid));
    }

    [Fact]
    public async Task Collectant_vinkt_eigen_adverteerders_af_en_meldt_een_nieuwe_aan()
    {
        var (alfredId, alfred) = await LidMetAccountAsync("Alfred Voorbeeld", "alfred@example.com", kader: true);
        var (_, gewoon) = await LidMetAccountAsync("Gewoon Lid", "gewoon@example.com", kader: false);
        await _bestuur.PutAsJsonAsync("/api/v1/admin/advertisers/campaign-year", new { year = 2027 });
        await _bestuur.PostAsync("/api/v1/admin/advertisers/import", Workbook(
            Row("Alfred Voorbeeld", 1, "Bakkerij De Test", "A", "C", null, null, 35, 35),
            Row("Iemand Anders", 2, "Garage Proef", "A", "C", null, null, 70, 70)));

        // Een gewoon lid is geen collectant en mag niets toevoegen.
        Assert.False((await gewoon.GetFromJsonAsync<JsonElement>("/api/v1/me/advertisers")).GetProperty("isCollector").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await gewoon.PostAsJsonAsync("/api/v1/me/advertisers", NewAdvertiser("Cash", null, false))).StatusCode);

        // De collectant ziet alleen zijn eigen adverteerder, met het bedrag van vorig jaar.
        var mine = await alfred.GetFromJsonAsync<JsonElement>("/api/v1/me/advertisers");
        Assert.True(mine.GetProperty("isCollector").GetBoolean());
        Assert.Equal(2027, mine.GetProperty("year").GetInt32());
        var item = Assert.Single(mine.GetProperty("items").EnumerateArray());
        Assert.Equal(("Bakkerij De Test", "Open", 35m), (item.GetProperty("companyName").GetString(), item.GetProperty("status").GetString(),
            item.GetProperty("previousAmount").GetDecimal()));
        var bakkerij = item.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await alfred.PutAsJsonAsync($"/api/v1/me/advertisers/{bakkerij}/status",
            new { status = "Collected", amount = 40m, note = (string?)null })).StatusCode);
        var garage = (await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/advertisers"))!.Single(a => a.GetProperty("number").GetInt32() == 2).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await alfred.PutAsJsonAsync($"/api/v1/me/advertisers/{garage}/status",
            new { status = "Stopped", amount = (decimal?)null, note = (string?)null })).StatusCode);

        // Nieuw via de app: met machtiging zijn IBAN en toestemming nodig; daarna opgehaald voor 2027.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await alfred.PostAsJsonAsync("/api/v1/me/advertisers", NewAdvertiser("Mandate", "NL91ABNA0417164300", false))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await alfred.PostAsJsonAsync("/api/v1/me/advertisers", NewAdvertiser("Mandate", "NL91ABNA0417164300", true))).StatusCode);

        var status = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/advertisers/status?year=2027&collector={alfredId}");
        Assert.Equal((2, 2, 90m), (status.GetProperty("totals").GetProperty("total").GetInt32(), status.GetProperty("totals").GetProperty("collected").GetInt32(),
            status.GetProperty("totals").GetProperty("collectedAmount").GetDecimal()));
        var added = (await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/advertisers"))!.Single(a => a.GetProperty("companyName").GetString() == "Nieuwe Zaak");
        Assert.Equal((3, true, true, "Alfred Voorbeeld"), (added.GetProperty("number").GetInt32(), added.GetProperty("addedViaApp").GetBoolean(),
            added.GetProperty("hasIban").GetBoolean(), added.GetProperty("collectorName").GetString()));
    }

    private static readonly System.Xml.Linq.XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.08";

    [Fact]
    public async Task Incasso_van_de_opgehaalde_adverteerders_met_machtiging_en_niet_dubbel()
    {
        var (_, alfred) = await LidMetAccountAsync("Alfred Voorbeeld", "alfred@example.com", kader: true);
        await _bestuur.PutAsJsonAsync("/api/v1/admin/collections/creditor",
            new { name = "CV De Vrolijke Drammers", iban = "NL39 RABO 0300 0652 64", creditorId = "nl12zzz123456780000" });
        await _bestuur.PutAsJsonAsync("/api/v1/admin/advertisers/campaign-year", new { year = 2027 });
        await _bestuur.PostAsync("/api/v1/admin/advertisers/import", Workbook(
            Row("Alfred Voorbeeld", 1, "Bakkerij De Test", "A", "M", "NL91 ABNA 0417 1643 00", "DVD000000001", 35, 35),
            Row("Alfred Voorbeeld", 2, "Garage Proef", "G", "C", null, null, 70, 70),
            Row("Alfred Voorbeeld", 3, "Kapsalon Zonder", "A", "M", null, "DVD000000003", 50, 50),
            Row("Alfred Voorbeeld", 4, "Café Nog Open", "A", "M", "NL91 ABNA 0417 1643 00", "DVD000000004", 25, 25)));

        var list = (await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/advertisers"))!;
        Guid Id(int number) => list.Single(a => a.GetProperty("number").GetInt32() == number).GetProperty("id").GetGuid();
        foreach (var number in new[] { 1, 2, 3 })
        {
            await _bestuur.PutAsJsonAsync($"/api/v1/admin/advertisers/{Id(number)}/years/2027", new { status = "Collected", amount = (decimal?)null, note = (string?)null });
        }

        // Nieuw via de app met machtiging: eerste incasso (FRST).
        await alfred.PostAsJsonAsync("/api/v1/me/advertisers", NewAdvertiser("Mandate", "NL91ABNA0417164300", true));

        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)).ToString("yyyy-MM-dd");
        var preview = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/advertisers/collections/preview?date={date}");
        Assert.Equal((2, 85m), (preview.GetProperty("count").GetInt32(), preview.GetProperty("total").GetDecimal()));
        var skipped = preview.GetProperty("skipped").EnumerateArray().Select(x => (x.GetProperty("fullName").GetString(), x.GetProperty("reason").GetString())).ToList();
        Assert.Contains(("Kapsalon Zonder", "Geen IBAN"), skipped);
        Assert.DoesNotContain(skipped, x => x.Item1 is "Garage Proef" or "Café Nog Open");

        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/advertisers/collections", new { date, year = (int?)null, description = (string?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var runId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var file = await _bestuur.GetAsync($"/api/v1/admin/advertisers/collections/{runId}/file");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.StartsWith("incasso-adverteerders-", file.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var xml = System.Xml.Linq.XDocument.Parse(await file.Content.ReadAsStringAsync());
        var sequences = xml.Descendants(Ns + "PmtInf").ToDictionary(p => p.Descendants(Ns + "SeqTp").Single().Value, p => p.Descendants(Ns + "MndtId").Select(m => m.Value).ToList());
        Assert.Equal(["DVD000000001"], sequences["RCUR"]);
        Assert.StartsWith("DVD-ADV-5-", Assert.Single(sequences["FRST"]));
        Assert.All(xml.Descendants(Ns + "DbtrAcct"), a => Assert.Equal("NL91ABNA0417164300", a.Descendants(Ns + "IBAN").Single().Value));
        Assert.Contains(xml.Descendants(Ns + "Ustrd"), u => u.Value == "Advertentie Drammerskrant 2027 CV De Vrolijke Drammers nr 1");

        // Niet dubbel: een tweede voorbeeld slaat ze over; de contributie-incasso ziet deze run niet.
        var again = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/advertisers/collections/preview?date={date}");
        Assert.Equal(0, again.GetProperty("count").GetInt32());
        Assert.Contains(again.GetProperty("skipped").EnumerateArray(), x => x.GetProperty("reason").GetString() == "Al in een incasso van 2027");
        Assert.Empty((await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/collections"))!);
        Assert.Single((await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/advertisers/collections"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await _bestuur.GetAsync($"/api/v1/admin/collections/{runId}/file")).StatusCode);
    }

    private static object NewAdvertiser(string payment, string? iban, bool consent) => new
    {
        companyName = "Nieuwe Zaak",
        contactName = "Nina Nieuw",
        phone = (string?)null,
        email = "zaak@example.com",
        addressLine = (string?)null,
        postalCode = (string?)null,
        city = "Loil",
        kind = "Advertisement",
        payment,
        amount = 50m,
        iban,
        mandateConsent = consent,
        note = (string?)null,
    };

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
