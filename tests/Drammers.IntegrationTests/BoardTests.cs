using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>
/// Bestuur en raad van elf (wens 2026-11-17): wie zit erin en hoe lang al. Het bestuur is verkiesbaar — een periode
/// duurt drie jaar en kan met één of twee jaar verlengd worden; de raad van elf heeft geen periode.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class BoardTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("secretaris@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> SeatAsync(object request)
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/board", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> SeatsAsync(string body, bool includePast = false) =>
        await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/board?body={body}&includePast={includePast}");

    [Fact]
    public async Task Bestuurder_met_drie_periodes_laat_zien_hoe_lang_hij_zit_en_tot_wanneer_de_periode_loopt()
    {
        var id = await SeatAsync(new
        {
            body = "Board",
            memberId = (Guid?)null,
            name = "Riet Voorzitter",
            function = "Voorzitter",
            startDate = "2019-03-01",
            endDate = (string?)null,
            notes = (string?)null,
            terms = new[]
            {
                new { startDate = "2019-03-01", extensionYears = 0, note = (string?)null },
                new { startDate = "2022-03-01", extensionYears = 0, note = (string?)null },
                new { startDate = "2025-03-01", extensionYears = 0, note = (string?)null },
            },
        });

        var seat = (await SeatsAsync("Board")).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);
        Assert.Equal("Voorzitter", seat.GetProperty("function").GetString());
        // Sinds 1 maart 2019: ruim zeven jaar, en de derde periode loopt tot 1 maart 2028.
        Assert.True(seat.GetProperty("daysServed").GetInt32() > 7 * 365);
        Assert.Equal("2028-03-01", seat.GetProperty("termEnd").GetString());
        var terms = seat.GetProperty("terms").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3], terms.Select(t => t.GetProperty("number").GetInt32()));
        Assert.Equal(["2022-03-01", "2025-03-01", "2028-03-01"], terms.Select(t => t.GetProperty("endDate").GetString()));
        Assert.Equal([false, false, true], terms.Select(t => t.GetProperty("isCurrent").GetBoolean()));
    }

    [Fact]
    public async Task Een_periode_verlengen_met_twee_jaar_en_daarna_herverkozen_worden()
    {
        var id = await SeatAsync(new
        {
            body = "Board",
            memberId = (Guid?)null,
            name = "Ton Penningmeester",
            function = "Penningmeester",
            startDate = "2024-03-01",
            endDate = (string?)null,
            notes = (string?)null,
            terms = (object?)null,
        });

        // Zonder periodes meegeven begint de eerste periode op de datum waarop hij in functie kwam.
        var seat = (await SeatsAsync("Board")).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);
        Assert.Equal("2027-03-01", seat.GetProperty("termEnd").GetString());

        // Verlengen kan met één of twee jaar; drie weigert het verzoek al bij de controle van het model (400).
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _bestuur.PostAsJsonAsync($"/api/v1/admin/board/{id}/extend", new { years = 3, note = (string?)null })).StatusCode);
        var extended = await (await _bestuur.PostAsJsonAsync($"/api/v1/admin/board/{id}/extend", new { years = 2, note = "tot na het jubileum" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2029-03-01", extended.GetProperty("termEnd").GetString());
        var current = extended.GetProperty("terms").EnumerateArray().Single(t => t.GetProperty("isCurrent").GetBoolean());
        Assert.Equal((2, "tot na het jubileum"), (current.GetProperty("extensionYears").GetInt32(), current.GetProperty("note").GetString()));

        // Herverkozen: een nieuwe periode van drie jaar, standaard aansluitend op de verlengde periode.
        var renewed = await (await _bestuur.PostAsJsonAsync($"/api/v1/admin/board/{id}/renew", new { startDate = (string?)null, note = (string?)null }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2032-03-01", renewed.GetProperty("termEnd").GetString());
        Assert.Equal(2, renewed.GetProperty("terms").GetArrayLength());
        // De verlenging van de vorige periode blijft staan: die hoort bij de jaren die al gemaakt zijn.
        Assert.Equal(2, renewed.GetProperty("terms")[0].GetProperty("extensionYears").GetInt32());
    }

    [Fact]
    public async Task De_raad_van_elf_heeft_geen_periodes_en_kan_stoppen_wanneer_hij_wil()
    {
        var id = await SeatAsync(new
        {
            body = "CouncilOfEleven",
            memberId = (Guid?)null,
            name = "Riet Raadslid",
            function = (string?)null,
            startDate = "2014-11-11",
            endDate = (string?)null,
            notes = (string?)null,
            // Een meegestuurde periode wordt genegeerd in plaats van geweigerd.
            terms = new[] { new { startDate = "2014-11-11", extensionYears = 1, note = (string?)null } },
        });

        var seat = (await SeatsAsync("CouncilOfEleven")).EnumerateArray().Single();
        Assert.Empty(seat.GetProperty("terms").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, seat.GetProperty("termEnd").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await _bestuur.PostAsJsonAsync($"/api/v1/admin/board/{id}/extend", new { years = 1, note = (string?)null })).StatusCode);

        // Gestopt: dan valt hij uit het overzicht, tenzij je ook het verleden opvraagt.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/board/{id}", new
        {
            body = "CouncilOfEleven",
            memberId = (Guid?)null,
            name = "Riet Raadslid",
            function = (string?)null,
            startDate = "2014-11-11",
            endDate = "2026-11-11",
            notes = "zelf gestopt na twaalf jaar",
            terms = (object?)null,
        })).StatusCode);
        Assert.Empty((await SeatsAsync("CouncilOfEleven")).EnumerateArray());
        var past = (await SeatsAsync("CouncilOfEleven", includePast: true)).EnumerateArray().Single();
        // De jaren stoppen op de einddatum: twaalf jaar, niet tot vandaag.
        Assert.Equal(Day("2026-11-11") - Day("2014-11-11"), past.GetProperty("daysServed").GetInt32());
    }

    [Fact]
    public async Task De_naam_volgt_de_ledenlijst_en_een_einddatum_voor_de_begindatum_kan_niet()
    {
        var memberId = await MemberAsync("Gerrie Lid");
        var id = await SeatAsync(new
        {
            body = "Board",
            memberId,
            name = "Typfout die niet gebruikt wordt",
            function = "Secretaris",
            startDate = "2026-03-01",
            endDate = (string?)null,
            notes = (string?)null,
            terms = (object?)null,
        });

        var seat = (await SeatsAsync("Board")).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == id);
        Assert.Equal("Gerrie Lid", seat.GetProperty("name").GetString());
        Assert.Equal(memberId, seat.GetProperty("memberId").GetGuid());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/board/{id}", new
        {
            body = "Board",
            memberId,
            name = (string?)null,
            function = "Secretaris",
            startDate = "2026-03-01",
            endDate = "2025-01-01",
            notes = (string?)null,
            terms = (object?)null,
        })).StatusCode);
    }

    [Fact]
    public async Task Een_gewoon_lid_ziet_het_bestuursoverzicht_niet()
    {
        var lid = _api.ClientFor((await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/board")).StatusCode);
    }

    private static int Day(string date) => DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber;

    private async Task<Guid> MemberAsync(string name)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var member = new Member
        {
            Id = IdGenerator.NewId(),
            MemberNumber = "0301",
            FullName = name,
            FirstName = name.Split(' ')[0],
            LastName = name.Split(' ')[^1],
            MembershipStatus = MembershipStatus.Active,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }
}
