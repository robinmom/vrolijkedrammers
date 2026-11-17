using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>
/// Carnavalsgroepen (wens 2026-11-17): het register van de groepen van het dorp. Een groep blijft over de jaren
/// bestaan; per jaar houden we bij hoe de wagen heette, in welke categorie ze zaten en op welke plaats ze eindigden.
/// Een inschrijving koppelt zichzelf bij exact dezelfde naam; anders koppelt de commissie hem in het portal.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CarnivalGroupTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private Guid _paradeId;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        var now = _api.Clock.UtcNow;
        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/parades", new
        {
            carnivalYearId = 1,
            name = "Optocht Loil 2027",
            paradeDate = "2027-02-07",
            startTime = "13:30:00",
            registrationOpensAt = now.AddDays(-1),
            registrationClosesAt = now.AddDays(20),
            subjectRequired = true,
            defaultSpacingMeters = 5m,
            maxDocumentsPerRegistration = 2,
            maxDocumentSizeMb = 5,
            status = "RegistrationOpen",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        _paradeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private async Task<Guid> GroupAsync(string name, int? since = null)
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/parade-groups", new
        {
            name,
            contactName = (string?)null,
            contactPhone = (string?)null,
            contactEmail = (string?)null,
            participatingSince = since,
            notes = (string?)null,
            active = true,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<JsonElement> DetailAsync(Guid id) => _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/parade-groups/{id}");

    private static JsonElement Year(JsonElement group, int year) =>
        group.GetProperty("years").EnumerateArray().Single(y => y.GetProperty("year").GetInt32() == year);

    /// <summary>Een ingediende inschrijving zoals de app hem achterlaat, zonder de hele inschrijfstroom.</summary>
    private async Task<Guid> RegistrationAsync(string groupName, string subject, int categoryId = 3, Guid? groupId = null)
    {
        var id = IdGenerator.NewId();
        await WithDbAsync(async db =>
        {
            db.ParadeRegistrations.Add(new ParadeRegistration
            {
                Id = id,
                ParadeId = _paradeId,
                CarnivalYearId = 1,
                RegistrationNumber = await db.ParadeRegistrations.CountAsync() + 1,
                CategoryId = categoryId,
                GroupName = groupName,
                ContactName = "Jan Jansen",
                ContactPhone = "+31612345678",
                ContactEmail = "jan@example.com",
                Subject = subject,
                AdultCount = 12,
                ChildrenCount = 4,
                Status = RegistrationStatus.Approved,
                Source = RegistrationSource.App,
                CarnivalGroupId = groupId,
                BuildAddress = new Address { Street = "Bosweg", HouseNumber = "3", PostalCode = "6999AA", City = "Loil" },
            });
            return await db.SaveChangesAsync();
        });
        return id;
    }

    [Fact]
    public async Task Inschrijving_koppelt_bij_indienen_zelf_aan_de_groep_met_dezelfde_naam()
    {
        // De naam wordt losgelaten op hoofdletters en leestekens: "C.V. de Kuitenbijters" is dezelfde groep.
        var groupId = await GroupAsync("CV de Kuitenbijters", 1998);
        var (_, oid) = await _api.CreateUserAsync("piet@example.com", DefaultRoles.Lid, DefaultRoles.Groepsverantwoordelijke);
        var lid = _api.ClientFor(oid);

        var draft = await (await lid.PostAsync("/api/v1/parade/registrations", null)).Content.ReadFromJsonAsync<JsonElement>();
        var registrationId = draft.GetProperty("id").GetGuid();
        await lid.PutAsJsonAsync($"/api/v1/parade/registrations/{registrationId}", new
        {
            version = draft.GetProperty("version").GetString(),
            groupName = "C.V. de Kuitenbijters",
            contactName = "Piet Lid",
            contactPhone = "06 12345678",
            contactEmail = "piet@example.com",
            categoryId = 3,
            subject = "Loil in de ruimte",
            childrenCount = 0,
            adultCount = 12,
            hasMusic = true,
            buildAddress = new { street = "Bosweg", houseNumber = "3", postalCode = "6999aa", city = "Loil", country = "NL" },
            juryInspectionSameAsBuildAddress = true,
            estimatedLengthMeters = 12.5m,
        });
        var submitted = await lid.PostAsync($"/api/v1/parade/registrations/{registrationId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        // Het jaar staat bij de groep, met het onderwerp als naam van de wagen en de gegevens van de inschrijving.
        var group = await DetailAsync(groupId);
        var year = Year(group, 2027);
        Assert.Equal("Loil in de ruimte", year.GetProperty("floatName").GetString());
        Assert.False(year.GetProperty("hasOwnRow").GetBoolean());
        var registration = year.GetProperty("registration");
        Assert.Equal("Piet Lid", registration.GetProperty("contactName").GetString());
        Assert.Equal("Bosweg 3, 6999 AA Loil", registration.GetProperty("buildAddress").GetString());
        Assert.Equal(12, registration.GetProperty("adultCount").GetInt32());

        // In het overzicht staat de groep als ingeschreven voor deze optocht.
        var list = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-groups");
        var row = list.EnumerateArray().Single(g => g.GetProperty("id").GetGuid() == groupId);
        Assert.True(row.GetProperty("registeredForCurrentParade").GetBoolean());
        Assert.Equal(1998, row.GetProperty("participatingSince").GetInt32());
        Assert.Equal(2027, row.GetProperty("lastYear").GetInt32());

        // En de inschrijving blijft buiten het lijstje "nog koppelen".
        Assert.Empty((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-groups/unlinked")).EnumerateArray());
    }

    [Fact]
    public async Task Inschrijving_met_een_afwijkende_naam_komt_op_het_lijstje_en_wordt_met_de_hand_gekoppeld()
    {
        var groupId = await GroupAsync("CV de Kuitenbijters");
        var registrationId = await RegistrationAsync("Kuitenbijters", "Loil in de ruimte");

        var unlinked = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-groups/unlinked");
        var open = unlinked.EnumerateArray().Single();
        Assert.Equal(registrationId, open.GetProperty("id").GetGuid());
        // Voorstel op naam: "kuitenbijters" zit in "cv de kuitenbijters".
        Assert.Equal(groupId, open.GetProperty("suggestedGroupId").GetGuid());

        var linked = await _bestuur.PostAsJsonAsync("/api/v1/admin/parade-groups/link", new { registrationId, groupId, createNew = false });
        Assert.Equal(groupId, (await linked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("groupId").GetGuid());
        Assert.Equal(2027, Year(await DetailAsync(groupId), 2027).GetProperty("year").GetInt32());
        Assert.Empty((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-groups/unlinked")).EnumerateArray());

        // Losmaken zet hem weer op het lijstje.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsJsonAsync("/api/v1/admin/parade-groups/unlink", new { registrationId })).StatusCode);
        Assert.Single((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-groups/unlinked")).EnumerateArray());
    }

    [Fact]
    public async Task Een_nieuwe_groep_maken_vanuit_een_inschrijving()
    {
        var registrationId = await RegistrationAsync("De Loopse Lopers", "Op de hei");

        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/parade-groups/link", new { registrationId, groupId = (Guid?)null, createNew = true });
        var groupId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("groupId").GetGuid();

        var group = await DetailAsync(groupId);
        Assert.Equal("De Loopse Lopers", group.GetProperty("name").GetString());
        // De contactgegevens uit de inschrijving staan meteen bij de groep.
        Assert.Equal("jan@example.com", group.GetProperty("contactEmail").GetString());
        Assert.Equal("Op de hei", Year(group, 2027).GetProperty("floatName").GetString());
    }

    [Fact]
    public async Task Jaren_van_voor_de_app_zelf_invullen_en_een_eigen_antwoord_gaat_voor_de_inschrijving()
    {
        var groupId = await GroupAsync("CV de Kuitenbijters");
        await RegistrationAsync("CV de Kuitenbijters", "Loil in de ruimte", groupId: groupId);

        // Een jaar zonder inschrijving: alleen wat je zelf invult.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/parade-groups/{groupId}/years/2009",
            new { floatName = "Drammers op de maan", categoryId = 1, place = 1, notes = "Eerste prijs" })).StatusCode);

        // En bij het jaar met inschrijving gaat de eigen naam vóór het onderwerp van de inschrijving.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/parade-groups/{groupId}/years/2027",
            new { floatName = "Toch iets anders", categoryId = (int?)null, place = (int?)null, notes = (string?)null })).StatusCode);

        var group = await DetailAsync(groupId);
        Assert.Equal([2027, 2009], group.GetProperty("years").EnumerateArray().Select(y => y.GetProperty("year").GetInt32()));
        Assert.Equal(2009, group.GetProperty("firstYear").GetInt32());
        var oud = Year(group, 2009);
        Assert.Equal(("Drammers op de maan", 1), (oud.GetProperty("floatName").GetString(), oud.GetProperty("place").GetInt32()));
        Assert.Equal(JsonValueKind.Null, oud.GetProperty("registration").ValueKind);
        var nu = Year(group, 2027);
        Assert.Equal("Toch iets anders", nu.GetProperty("floatName").GetString());
        // De categorie blijft die van de inschrijving zolang je er zelf geen kiest.
        Assert.Equal(3, nu.GetProperty("categoryId").GetInt32());

        // Het jaar weghalen laat de inschrijving staan.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.DeleteAsync($"/api/v1/admin/parade-groups/{groupId}/years/2027")).StatusCode);
        Assert.Equal("Loil in de ruimte", Year(await DetailAsync(groupId), 2027).GetProperty("floatName").GetString());
    }

    [Fact]
    public async Task Twee_groepen_met_dezelfde_naam_kan_niet_en_verwijderen_mag_niet_met_een_inschrijving()
    {
        var groupId = await GroupAsync("CV de Kuitenbijters");
        var duplicate = await _bestuur.PostAsJsonAsync("/api/v1/admin/parade-groups", new
        {
            name = "C.V.  DE KUITENBIJTERS",
            contactName = (string?)null,
            contactPhone = (string?)null,
            contactEmail = (string?)null,
            participatingSince = (int?)null,
            notes = (string?)null,
            active = true,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Zonder inschrijving kan verwijderen; met een inschrijving niet, want dan raakt de geschiedenis los.
        var leeg = await GroupAsync("De Loopse Lopers");
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.DeleteAsync($"/api/v1/admin/parade-groups/{leeg}")).StatusCode);
        await RegistrationAsync("CV de Kuitenbijters", "Loil in de ruimte", groupId: groupId);
        var blocked = await _bestuur.DeleteAsync($"/api/v1/admin/parade-groups/{groupId}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // Een groep die niet meer meedoet valt uit het overzicht en koppelt niet meer automatisch.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/parade-groups/{groupId}", new
        {
            name = "CV de Kuitenbijters",
            contactName = (string?)null,
            contactPhone = (string?)null,
            contactEmail = (string?)null,
            participatingSince = (int?)null,
            notes = (string?)null,
            active = false,
        })).StatusCode);
        Assert.Empty((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-groups")).EnumerateArray());
        Assert.Single((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-groups?includeInactive=true")).EnumerateArray());
    }

    [Fact]
    public async Task De_plaats_komt_uit_de_uitslag_zodra_die_gepubliceerd_is()
    {
        var groupId = await GroupAsync("CV de Kuitenbijters");
        var registrationId = await RegistrationAsync("CV de Kuitenbijters", "Loil in de ruimte", groupId: groupId);

        // Eén jurylid voor de categorie van de inzending; de jury-instellingen ontstaan bij het openen van de pagina.
        await _bestuur.GetAsync("/api/v1/admin/jury");
        var (jurorId, jurorOid) = await _api.CreateUserAsync("jurylid@example.com", DefaultRoles.Jury);
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/jurors/{jurorId}/categories", new { categoryIds = new[] { 3 } });
        var jurylid = _api.ClientFor(jurorOid);
        var now = _api.Clock.UtcNow;
        var scores = Enum.GetNames<Drammers.Modules.Parade.Judging.JudgingCriterion>()
            .Select(criterion => new { registrationId, pass = 1, criterion, value = 80, scoredAt = now.UtcDateTime });
        Assert.True((await jurylid.PutAsJsonAsync($"/api/v1/jury/parades/{_paradeId}/scores", new { scores })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await jurylid.PostAsync($"/api/v1/jury/parades/{_paradeId}/submit", null)).StatusCode);

        var uitslag = _api.ClientFor((await _api.CreateUserAsync("uitslag@example.com", DefaultRoles.Uitslagcommissie)).ObjectId);
        Assert.Equal(HttpStatusCode.OK,
            (await uitslag.PostAsJsonAsync("/api/v1/admin/results/publish", new { paradeId = _paradeId, prizeCeremonyHeld = true })).StatusCode);

        var year = Year(await DetailAsync(groupId), 2027);
        Assert.Equal(1, year.GetProperty("place").GetInt32());
        Assert.True(year.GetProperty("placeFromResults").GetBoolean());

        // Zelf invullen gaat vóór de uitslag en blijft daarna staan.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/parade-groups/{groupId}/years/2027",
            new { floatName = (string?)null, categoryId = (int?)null, place = 4, notes = "jury had het mis" })).StatusCode);
        var corrected = Year(await DetailAsync(groupId), 2027);
        Assert.Equal(4, corrected.GetProperty("place").GetInt32());
        Assert.False(corrected.GetProperty("placeFromResults").GetBoolean());
    }

    [Fact]
    public async Task Het_register_is_voor_de_optochtcommissie_en_niet_voor_een_gewoon_lid()
    {
        var groupId = await GroupAsync("CV de Kuitenbijters");
        var commissie = _api.ClientFor((await _api.CreateUserAsync("commissie@example.com", DefaultRoles.Optochtcommissie)).ObjectId);
        Assert.Equal(HttpStatusCode.OK, (await commissie.GetAsync("/api/v1/admin/parade-groups")).StatusCode);

        var lid = _api.ClientFor((await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/parade-groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync($"/api/v1/admin/parade-groups/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.DeleteAsync($"/api/v1/admin/parade-groups/{groupId}")).StatusCode);
    }
}
