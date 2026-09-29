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

/// <summary>Fase 16: aanrijtijden van de wagens — genereren, aanpassen, importeren (websitetabel) en publiceren.</summary>
[Collection(SqlServerCollection.Name)]
public class ParadeArrivalsTests(SqlServerFixture sql) : IAsyncLifetime
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
        string email, string group, bool? music = true, bool approve = true, bool sameJury = true, int category = 1)
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
            categoryId = category,
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

    private Task<HttpResponseMessage> StartNumberAsync(Guid id, int number) =>
        _commissie.PutAsJsonAsync($"/api/v1/admin/parade-registrations/{id}/start-number", new { startNumber = number, swap = false });

    private async Task<(Guid WagonA, Guid WagonB, Guid Walkers, HttpClient LidA)> LineupAsync()
    {
        var (a, lidA, _) = await RegistrationAsync("a@example.com", "De Snotapen");
        var (b, _, _) = await RegistrationAsync("b@example.com", "De Sökkels", category: 2);
        var (w, _, _) = await RegistrationAsync("w@example.com", "De Lopers", category: 3);
        await JsonAsync(await StartNumberAsync(a, 5), HttpStatusCode.NoContent);
        await JsonAsync(await StartNumberAsync(b, 4), HttpStatusCode.NoContent);
        await JsonAsync(await StartNumberAsync(w, 6), HttpStatusCode.NoContent);
        return (a, b, w, lidA);
    }

    [Fact]
    public async Task Genereren_aanpassen_en_publiceren_alleen_wagens_en_pas_daarna_zichtbaar()
    {
        var (a, b, _, lidA) = await LineupAsync();

        var list = await JsonAsync(await _commissie.GetAsync("/api/v1/admin/parade/arrival-times"));
        Assert.Equal("Rotonde Holthuizen", list.GetProperty("location").GetString());
        Assert.Equal(["De Sökkels", "De Snotapen"], list.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("groupName").GetString()));

        var generated = await JsonAsync(await _commissie.PostAsJsonAsync("/api/v1/admin/parade/arrival-times/generate", new { first = "10:30:00", intervalMinutes = 4, onlyEmpty = false }));
        Assert.Equal(2, generated.GetProperty("changed").GetInt32());
        var times = await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.Id == a || r.Id == b).ToDictionaryAsync(r => r.Id, r => r.ArrivalTime));
        Assert.Equal((new TimeOnly(10, 34), new TimeOnly(10, 30)), (times[a], times[b]));

        // Nog niet gepubliceerd: niet zichtbaar voor de groep en niet openbaar.
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await lidA.GetAsync($"/api/v1/parade/registrations/{a}"))).GetProperty("arrivalTime").ValueKind);
        var anonymous = _api.CreateClient();
        Assert.False((await JsonAsync(await anonymous.GetAsync("/api/v1/parade/arrival-times"))).GetProperty("published").GetBoolean());

        await JsonAsync(await _commissie.PutAsJsonAsync($"/api/v1/admin/parade/arrival-times/{a}", new { arrivalTime = "10:40:00" }), HttpStatusCode.NoContent);
        var published = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/arrival-times/publish", null));
        Assert.Equal(2, published.GetProperty("changed").GetInt32());
        Assert.Equal(2, await WithDbAsync(db => db.Notifications.CountAsync(n => n.Title == "Aanrijtijd optocht bekend")));
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Body.Contains("10:40 uur bij Rotonde Holthuizen"))));

        var mine = await JsonAsync(await lidA.GetAsync($"/api/v1/parade/registrations/{a}"));
        Assert.Equal(("10:40", "Rotonde Holthuizen"), (mine.GetProperty("arrivalTime").GetString(), mine.GetProperty("arrivalLocation").GetString()));
        var open = await JsonAsync(await anonymous.GetAsync("/api/v1/parade/arrival-times"));
        Assert.True(open.GetProperty("published").GetBoolean());
        Assert.Equal([(4, "De Sökkels", "10:30"), (5, "De Snotapen", "10:40")], open.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("startNumber").GetInt32(), r.GetProperty("groupName").GetString(), r.GetProperty("arrivalTime").GetString())));
        Assert.DoesNotContain("De Lopers", open.GetRawText(), StringComparison.Ordinal);

        // Na publiceren hoort de groep een wijziging direct; een loopgroep krijgt geen tijd.
        await JsonAsync(await _commissie.PutAsJsonAsync($"/api/v1/admin/parade/arrival-times/{a}", new { arrivalTime = "11:00:00" }), HttpStatusCode.NoContent);
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title == "Aanrijtijd optocht gewijzigd" && n.Body.Contains("11:00"))));
        var walkers = await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.GroupName == "De Lopers").Select(r => r.Id).SingleAsync());
        await JsonAsync(await _commissie.PutAsJsonAsync($"/api/v1/admin/parade/arrival-times/{walkers}", new { arrivalTime = "11:00:00" }), HttpStatusCode.NotFound);

        var (_, lidOid) = await _api.CreateUserAsync("gewoon@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lidOid).GetAsync("/api/v1/admin/parade/arrival-times")).StatusCode);
    }

    [Fact]
    public async Task Importeren_uit_de_websitetabel_met_de_meldplek_als_kolomkop()
    {
        var (a, b, _, _) = await LineupAsync();

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Aanrijtijden");
        string[] headers = ["Stnr.", "Categorie", "Naam", "Parkeerplaats De Muggenhof"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        sheet.Cell(2, 1).Value = 4;
        sheet.Cell(2, 4).Value = "10:30 uur";
        sheet.Cell(3, 1).Value = 5;
        sheet.Cell(3, 4).Value = TimeSpan.FromMinutes((10 * 60) + 34);
        sheet.Cell(4, 1).Value = 6;
        sheet.Cell(4, 4).Value = "10:38 uur";
        sheet.Cell(5, 1).Value = 7;
        sheet.Cell(5, 4).Value = "later";

        var preview = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/arrival-times/import/preview", File(workbook, "aanrijtijden.xlsx")));
        Assert.Equal("Parkeerplaats De Muggenhof", preview.GetProperty("location").GetString());
        var errors = preview.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("message").GetString()!).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("Startnummer 6 is De Lopers; dat is geen wagen", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Geen groep in de optocht heeft startnummer 7", StringComparison.Ordinal));
        await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/arrival-times/import", File(workbook, "aanrijtijden.xlsx")), HttpStatusCode.UnprocessableEntity);

        sheet.Row(5).Delete();
        sheet.Row(4).Delete();
        var ok = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/arrival-times/import/preview", File(workbook, "aanrijtijden.xlsx")));
        Assert.Empty(ok.GetProperty("errors").EnumerateArray());
        Assert.Equal(2, ok.GetProperty("changes").GetArrayLength());
        var done = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade/arrival-times/import", File(workbook, "aanrijtijden.xlsx")));
        Assert.Equal(2, done.GetProperty("changed").GetInt32());
        var times = await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.Id == a || r.Id == b).ToDictionaryAsync(r => r.Id, r => r.ArrivalTime));
        Assert.Equal((new TimeOnly(10, 34), new TimeOnly(10, 30)), (times[a], times[b]));
        Assert.Equal("Parkeerplaats De Muggenhof", (await JsonAsync(await _commissie.GetAsync("/api/v1/admin/parade/arrival-times"))).GetProperty("location").GetString());
    }

    [Theory]
    [InlineData("10:30", 10, 30)]
    [InlineData("10.30 uur", 10, 30)]
    [InlineData("9:05", 9, 5)]
    [InlineData("1030", 10, 30)]
    [InlineData("0.4375", 10, 30)]
    public void Tijden_zoals_ze_in_Excel_of_op_de_website_staan(string input, int hour, int minute) =>
        Assert.Equal(new TimeOnly(hour, minute), Drammers.Infrastructure.ParadeManagement.ParadeArrivals.ParseTime(input));
}
