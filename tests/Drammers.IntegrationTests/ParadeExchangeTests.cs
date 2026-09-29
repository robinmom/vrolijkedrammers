using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 12c: vaste plekken vooraan, de vraag Muziek, de export in het deelnemersformaat en startnummers importeren.</summary>
[Collection(SqlServerCollection.Name)]
public class ParadeExchangeTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _commissie = null!;
    private Guid _paradeId;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        var bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        var now = _api.Clock.UtcNow;
        var created = await JsonAsync(await bestuur.PostAsJsonAsync("/api/v1/admin/parades", new
        {
            carnivalYearId = 1,
            name = "Optocht Loil 2027",
            paradeDate = "2027-02-07",
            startTime = "13:30:00",
            registrationOpensAt = now.AddDays(-1),
            registrationClosesAt = now.AddDays(20),
            editDeadlineAt = (DateTimeOffset?)null,
            subjectRequired = true,
            defaultSpacingMeters = 5m,
            maxDocumentsPerRegistration = 2,
            maxDocumentSizeMb = 5,
            status = "RegistrationOpen",
        }), HttpStatusCode.Created);
        _paradeId = created.GetProperty("id").GetGuid();
        var (_, commissieOid) = await _api.CreateUserAsync("commissie@example.com", DefaultRoles.Lid, DefaultRoles.Optochtcommissie);
        _commissie = _api.ClientFor(commissieOid);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return response.StatusCode == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Een lid schrijft in via de app; standaard dient het in en keurt de commissie goed.</summary>
    private async Task<(Guid Id, HttpClient Lid, JsonElement Draft)> RegistrationAsync(
        string email, string group, bool? music = true, bool approve = true, bool sameJury = true)
    {
        var (userId, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid, DefaultRoles.Groepsverantwoordelijke);
        var memberId = IdGenerator.NewId();
        await WithDbAsync(async db =>
        {
            db.Members.Add(new Member
            {
                Id = memberId,
                MemberNumber = email[..4],
                FullName = "Piet Lid",
                AddressLine = "Kerkstraat 3",
                PostalCode = "6999 AB",
                City = "Loil",
                MembershipStatus = MembershipStatus.Active,
            });
            await db.SaveChangesAsync();
            return await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.MemberId, memberId));
        });
        var lid = _api.ClientFor(oid);
        var draft = await JsonAsync(await lid.PostAsync("/api/v1/parade/registrations", null), HttpStatusCode.Created);
        var id = draft.GetProperty("id").GetGuid();
        var saved = await JsonAsync(await lid.PutAsJsonAsync($"/api/v1/parade/registrations/{id}", new
        {
            version = draft.GetProperty("version").GetString(),
            groupName = group,
            contactName = "Piet Lid",
            contactPhone = "0612345678",
            contactEmail = email,
            categoryId = 3,
            subject = "Wilde westen",
            subjectDescription = "Cowboys en indianen trekken door Loil.",
            childrenCount = 2,
            adultCount = 12,
            hasMusic = music,
            buildAddress = new { street = "Truisweg", houseNumber = "4", postalCode = "6999 AA", city = "Loil", country = "NL" },
            juryInspectionSameAsBuildAddress = sameJury,
            juryInspectionAddress = sameJury ? null : new { street = "Paltsweg", houseNumber = "5", postalCode = "6999 AC", city = "Loil", country = "NL" },
            estimatedLengthMeters = 12.5m,
            additionalInformation = "Lopen achter de Duuvels",
        }));
        if (approve)
        {
            await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null));
            await JsonAsync(await _commissie.PostAsJsonAsync($"/api/v1/admin/parade-registrations/{id}/review", new { action = "Approve" }), HttpStatusCode.NoContent);
        }

        return (id, lid, saved);
    }

    private static MultipartFormDataContent File(XLWorkbook workbook, string name = "startnummers.xlsx", int? version = null)
    {
        // Niet sluiten: ClosedXML gebruikt de laatst opgeslagen stream als bron bij een volgende opslag.
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var file = new ByteArrayContent(stream.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var form = new MultipartFormDataContent { { file, "file", name } };
        if (version is { } v)
        {
            form.Add(new StringContent(v.ToString(System.Globalization.CultureInfo.InvariantCulture)), "version");
        }

        return form;
    }

    private async Task<XLWorkbook> ExportAsync()
    {
        var response = await _commissie.GetAsync("/api/v1/admin/parade/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Opgaven optocht 2027", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName, StringComparison.Ordinal);
        // Via een tijdelijk bestand: een werkmap die uit een stream is geopend, kan ClosedXML niet opnieuw opslaan.
        var path = Path.Combine(Path.GetTempPath(), $"optocht-{Guid.NewGuid():N}.xlsx");
        await System.IO.File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync());
        return new XLWorkbook(path);
    }

    [Fact]
    public async Task Nieuwe_optocht_heeft_drie_vaste_plekken_die_startnummers_1_tot_3_reserveren()
    {
        var parade = await JsonAsync(await _commissie.GetAsync($"/api/v1/admin/parades/{_paradeId}"));
        Assert.Equal(["Geluidswagen", "Verenigingswagen \"de Vrolijke Drammers\"", "Het Convent van \"de Vrolijke Drammers\""],
            parade.GetProperty("fixedEntries").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
        var raw = await WithDbAsync(db => db.Database.SqlQueryRaw<string>("SELECT fixed_entries AS [Value] FROM parade.Parade").SingleAsync());
        Assert.Contains("Geluidswagen", raw, StringComparison.Ordinal);

        var (a, _, _) = await RegistrationAsync("a@example.com", "De Knotwilgen");
        var reserved = await JsonAsync(await _commissie.PutAsJsonAsync($"/api/v1/admin/parade-registrations/{a}/start-number", new { startNumber = 2, swap = false }), HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Verenigingswagen", reserved.GetProperty("detail").GetString(), StringComparison.Ordinal);
        await JsonAsync(await _commissie.PutAsJsonAsync($"/api/v1/admin/parade-registrations/{a}/start-number", new { startNumber = 4, swap = false }), HttpStatusCode.NoContent);

        // Uitbreiden naar 4 vaste plekken botst met de groep op 4.
        var entries = new[] { "Geluidswagen", "Verenigingswagen", "Convent", "Prinsenwagen" }.Select(n => new { name = n, adultCount = 1, childrenCount = 0, hasMusic = true }).ToArray();
        var conflict = await JsonAsync(await _commissie.PutAsJsonAsync($"/api/v1/admin/parades/{_paradeId}/fixed-entries", new { entries }), HttpStatusCode.Conflict);
        Assert.Contains("De Knotwilgen", conflict.GetProperty("detail").GetString(), StringComparison.Ordinal);
        await JsonAsync(await _commissie.PutAsJsonAsync($"/api/v1/admin/parades/{_paradeId}/fixed-entries", new { entries = entries.Take(2) }), HttpStatusCode.NoContent);
        var (_, lidOid) = await _api.CreateUserAsync("gewoon@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lidOid).PutAsJsonAsync($"/api/v1/admin/parades/{_paradeId}/fixed-entries", new { entries })).StatusCode);
        var composition = await JsonAsync(await _commissie.GetAsync("/api/v1/admin/parade-composition"));
        Assert.Equal(3, composition.GetProperty("firstStartNumber").GetInt32());
    }

    [Fact]
    public async Task Muziek_is_verplicht_bij_indienen()
    {
        var (id, lid, _) = await RegistrationAsync("m@example.com", "De Stillen", music: null, approve: false);
        var submit = await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null), HttpStatusCode.UnprocessableEntity);
        Assert.Contains("hasMusic", submit.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_in_het_deelnemersformaat_met_vaste_plekken_eerst()
    {
        await RegistrationAsync("a@example.com", "De Knotwilgen");
        await RegistrationAsync("b@example.com", "De Bouwers", music: false, sameJury: false);
        await RegistrationAsync("c@example.com", "Nog in concept", approve: false);

        using var workbook = await ExportAsync();
        var sheet = workbook.Worksheet(1);
        Assert.Equal("deelnemersbestand 2027", sheet.Name);
        string[] headers =
        [
            "Opgave", "Startnummer", "Naam groep", "contactpersoon", "adres", "tel nr", "mail adres", "Categorie", "Soort", "onderwerp", "kinderen",
            "volwassenen", "Muziek", "Bouw adres", "Stalling voor jury", "Lengte", "Extra info", "Tekst",
        ];
        Assert.Equal(headers, Enumerable.Range(1, 18).Select(c => sheet.Cell(1, c).GetString()));
        // Opmaak zoals het bronbestand: regel 2 leeg, vaste plekken op 3–5, regel 6 leeg, kolom E (adres) verborgen, Arial 10.
        Assert.True(sheet.Row(2).IsEmpty());
        Assert.Equal(("", 1, "Geluidswagen", "Ja"), (sheet.Cell(3, 1).GetString(), sheet.Cell(3, 2).GetValue<int>(), sheet.Cell(3, 3).GetString(), sheet.Cell(3, 13).GetString()));
        Assert.Equal("Het Convent van \"de Vrolijke Drammers\"", sheet.Cell(5, 3).GetString());
        Assert.True(sheet.Row(6).IsEmpty());
        Assert.True(sheet.Column(5).IsHidden);
        Assert.Equal(("Arial", 10d), (sheet.Cell(7, 3).Style.Font.FontName, sheet.Cell(7, 3).Style.Font.FontSize));
        Assert.Equal(5d, sheet.Column(1).Width, 1);

        var knotwilgen = sheet.Row(7);
        Assert.Equal(1, knotwilgen.Cell(1).GetValue<int>());
        Assert.Equal("De Knotwilgen", knotwilgen.Cell(3).GetString());
        Assert.Equal("Kerkstraat 3, 6999 AB Loil", knotwilgen.Cell(5).GetString());
        Assert.Equal("06 12345678", knotwilgen.Cell(6).GetString());
        Assert.Equal("Volwassenen", knotwilgen.Cell(9).GetString());
        Assert.Equal((2, 12, "Ja"), (knotwilgen.Cell(11).GetValue<int>(), knotwilgen.Cell(12).GetValue<int>(), knotwilgen.Cell(13).GetString()));
        Assert.Equal(("Truisweg 4", "nvt", 12.5m), (knotwilgen.Cell(14).GetString(), knotwilgen.Cell(15).GetString(), knotwilgen.Cell(16).GetValue<decimal>()));
        Assert.Equal(("Lopen achter de Duuvels", "Cowboys en indianen trekken door Loil."), (knotwilgen.Cell(17).GetString(), knotwilgen.Cell(18).GetString()));
        var bouwers = sheet.Row(8);
        Assert.Equal(("De Bouwers", "Nee", "Paltsweg 5"), (bouwers.Cell(3).GetString(), bouwers.Cell(13).GetString(), bouwers.Cell(15).GetString()));
        Assert.True(sheet.Row(9).IsEmpty());
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "parade.exported")));
    }

    [Fact]
    public async Task Startnummers_importeren_voorbeeld_met_fouten_leest_niets_in_en_daarna_alles_of_niets()
    {
        var (a, _, _) = await RegistrationAsync("a@example.com", "De Knotwilgen");
        var (b, _, _) = await RegistrationAsync("b@example.com", "De Bouwers");
        var (c, _, _) = await RegistrationAsync("c@example.com", "Nog niet goedgekeurd", approve: false);
        await JsonAsync(await _api.ClientFor(await WithDbAsync(db => db.Users.Where(u => u.Email == "c@example.com").Select(u => u.ExternalObjectId).SingleAsync()))
            .PostAsync($"/api/v1/parade/registrations/{c}/submit", null));

        using var workbook = await ExportAsync();
        var sheet = workbook.Worksheet(1);
        // Rij 7 = opgave 1, rij 8 = opgave 2, rij 9 = opgave 3 (ingediend, niet goedgekeurd).
        sheet.Cell(7, 2).Value = 2;          // vaste plek
        sheet.Cell(8, 2).Value = "tien";     // geen nummer
        sheet.Cell(9, 2).Value = 9;          // niet goedgekeurd
        sheet.Cell(10, 1).Value = 42;        // onbekende opgave
        var preview = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/start-numbers/import/preview", File(workbook)));
        var errors = preview.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("message").GetString()!).ToList();
        Assert.Equal(4, errors.Count);
        Assert.Contains(errors, e => e.Contains("vaste plek", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("\"tien\"", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("niet goedgekeurd", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Opgave 42 bestaat niet", StringComparison.Ordinal));
        var version = preview.GetProperty("version").GetInt32();
        await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/start-numbers/import", File(workbook, version: version)), HttpStatusCode.UnprocessableEntity);
        Assert.False(await WithDbAsync(db => db.ParadeRegistrations.AnyAsync(r => r.StartNumber != null)));

        // Dubbel startnummer wordt ook gevonden.
        sheet.Cell(10, 1).Clear();
        sheet.Cell(9, 2).Clear();
        sheet.Cell(7, 2).Value = 7;
        sheet.Cell(8, 2).Value = 7;
        var duplicate = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/start-numbers/import/preview", File(workbook)));
        Assert.Contains("Startnummer 7 komt meer dan eens voor", duplicate.GetProperty("errors")[0].GetProperty("message").GetString(), StringComparison.Ordinal);

        sheet.Cell(8, 2).Value = 4;
        var ok = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/start-numbers/import/preview", File(workbook)));
        Assert.Empty(ok.GetProperty("errors").EnumerateArray());
        Assert.Equal([(2, 4), (1, 7)], ok.GetProperty("changes").EnumerateArray().Select(x => (x.GetProperty("registrationNumber").GetInt32(), x.GetProperty("newStartNumber").GetInt32())));

        // Intussen gewijzigd: 412, dan opnieuw met de juiste versie.
        await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/start-numbers/import", File(workbook, version: ok.GetProperty("version").GetInt32() + 1)), HttpStatusCode.PreconditionFailed);
        var done = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/start-numbers/import", File(workbook, version: ok.GetProperty("version").GetInt32())));
        Assert.Equal(2, done.GetProperty("changed").GetInt32());
        var numbers = await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.Id == a || r.Id == b).ToDictionaryAsync(r => r.Id, r => r.StartNumber));
        Assert.Equal((7, 4), (numbers[a], numbers[b]));
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(l => l.Action == "parade.start-numbers-imported")));

        // Hetzelfde bestand nogmaals: geen wijzigingen.
        var again = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/start-numbers/import/preview", File(workbook)));
        Assert.Empty(again.GetProperty("changes").EnumerateArray());

        // Alleen .xlsx.
        var text = new MultipartFormDataContent { { new ByteArrayContent("Opgave;Startnummer"u8.ToArray()), "file", "lijst.csv" } };
        await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/start-numbers/import/preview", text), HttpStatusCode.UnprocessableEntity);
    }
}
