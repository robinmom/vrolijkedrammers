using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 12a: overzicht met filters en totalen, startnummers toekennen/wisselen/publiceren en de gemeten lengte.</summary>
[Collection(SqlServerCollection.Name)]
public class ParadeLineupTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _commissie = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        var bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        var now = _api.Clock.UtcNow;
        await JsonAsync(await bestuur.PostAsJsonAsync("/api/v1/admin/parades", new
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

    /// <summary>Een groep schrijft in via de app en de commissie keurt goed.</summary>
    private async Task<Guid> ApprovedAsync(string email, string group, int category = 3, int adults = 12, decimal length = 12.5m)
    {
        var (_, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid, DefaultRoles.Groepsverantwoordelijke);
        var lid = _api.ClientFor(oid);
        var draft = await JsonAsync(await lid.PostAsync("/api/v1/parade/registrations", null), HttpStatusCode.Created);
        var id = draft.GetProperty("id").GetGuid();
        var address = new { street = "Dorpsstraat", houseNumber = "1", postalCode = "6999 AA", city = "Loil", country = "NL" };
        await JsonAsync(await lid.PutAsJsonAsync($"/api/v1/parade/registrations/{id}", new
        {
            version = draft.GetProperty("version").GetString(),
            groupName = group,
            contactName = "Piet Lid",
            contactPhone = "0612345678",
            contactEmail = email,
            categoryId = category,
            subject = "Wilde westen",
            childrenCount = 0,
            adultCount = adults,
            buildAddress = address,
            juryInspectionSameAsBuildAddress = true,
            estimatedLengthMeters = length,
        }));
        await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null));
        await JsonAsync(await _commissie.PostAsJsonAsync($"/api/v1/admin/parade-registrations/{id}/review", new { action = "Approve" }), HttpStatusCode.NoContent);
        return id;
    }

    private Task<HttpResponseMessage> StartNumberAsync(Guid id, int? number, bool swap = false) =>
        _commissie.PutAsJsonAsync($"/api/v1/admin/parade-registrations/{id}/start-number", new { startNumber = number, swap });

    [Fact]
    public async Task Bezet_startnummer_geeft_de_naam_van_de_andere_groep_en_wisselen_lost_het_op()
    {
        var a = await ApprovedAsync("a@example.com", "De Knotwilgen");
        var b = await ApprovedAsync("b@example.com", "De Bouwers");
        await JsonAsync(await StartNumberAsync(a, 17), HttpStatusCode.NoContent);
        await JsonAsync(await StartNumberAsync(b, 20), HttpStatusCode.NoContent);

        var taken = await JsonAsync(await StartNumberAsync(b, 17), HttpStatusCode.Conflict);
        Assert.Equal("START_NUMBER_TAKEN", taken.GetProperty("code").GetString());
        Assert.Contains("De Knotwilgen", taken.GetProperty("detail").GetString(), StringComparison.Ordinal);

        await JsonAsync(await StartNumberAsync(b, 17, swap: true), HttpStatusCode.NoContent);
        var numbers = await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.Id == a || r.Id == b).ToDictionaryAsync(r => r.Id, r => r.StartNumber));
        Assert.Equal((20, 17), (numbers[a], numbers[b]));
        var history = await WithDbAsync(db => db.ParadeRegistrationHistory.Where(h => h.RegistrationId == a && h.FieldName == "StartNumber")
            .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id).Select(h => new { h.OldValue, h.NewValue }).ToListAsync());
        Assert.Equal([(null, "17"), ("17", "20")], history.Select(h => (h.OldValue, h.NewValue)));
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(l => l.Action == "parade-registration.start-number-swapped")));

        var (_, lidOid) = await _api.CreateUserAsync("gewoon@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lidOid).PutAsJsonAsync($"/api/v1/admin/parade-registrations/{a}/start-number", new { startNumber = 1 })).StatusCode);
    }

    [Fact]
    public async Task Publiceren_meldt_elke_groep_haar_startnummer_en_een_latere_wijziging_ook()
    {
        var a = await ApprovedAsync("a@example.com", "De Knotwilgen");
        var b = await ApprovedAsync("b@example.com", "De Bouwers");
        await JsonAsync(await StartNumberAsync(a, 1), HttpStatusCode.NoContent);

        var result = await JsonAsync(await _commissie.PostAsync("/api/v1/admin/parade-registrations/publish-start-numbers", null));
        Assert.Equal((1, 1), (result.GetProperty("published").GetInt32(), result.GetProperty("withoutStartNumber").GetInt32()));
        var statuses = await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.Id == a || r.Id == b).ToDictionaryAsync(r => r.Id, r => r.Status.ToString()));
        Assert.Equal(("StartNumberAssigned", "Approved"), (statuses[a], statuses[b]));
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title == "Startnummer optocht bekend" && n.Body.Contains("startnummer is 1"))));

        await JsonAsync(await StartNumberAsync(a, 5), HttpStatusCode.NoContent);
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title == "Startnummer optocht gewijzigd" && n.Body.Contains("startnummer 5"))));
        // Twee keer goedkeuren, één keer publiceren en één wijziging: vier statusmails.
        Assert.Equal(4, await WithDbAsync(db => db.Outbox.CountAsync(m => m.Type == "parade.status-mail")));
    }

    [Fact]
    public async Task Alleen_goedgekeurde_groepen_krijgen_een_startnummer()
    {
        var (_, oid) = await _api.CreateUserAsync("c@example.com", DefaultRoles.Lid, DefaultRoles.Groepsverantwoordelijke);
        var draft = await JsonAsync(await _api.ClientFor(oid).PostAsync("/api/v1/parade/registrations", null), HttpStatusCode.Created);

        Assert.Equal(HttpStatusCode.NotFound, (await StartNumberAsync(draft.GetProperty("id").GetGuid(), 3)).StatusCode);
        var a = await ApprovedAsync("a@example.com", "De Knotwilgen");
        await JsonAsync(await _commissie.PostAsJsonAsync($"/api/v1/admin/parade-registrations/{a}/review", new { action = "Reopen", reason = "Toch nog even kijken." }), HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Conflict, (await StartNumberAsync(a, 3)).StatusCode);
    }

    [Fact]
    public async Task Gemeten_lengte_filters_en_totalen()
    {
        var a = await ApprovedAsync("a@example.com", "De Knotwilgen", length: 12.5m);
        await ApprovedAsync("b@example.com", "De Bouwers", category: 4, adults: 4, length: 6m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await _commissie.PutAsJsonAsync($"/api/v1/admin/parade-registrations/{a}/measured-length", new { measuredLengthMeters = 12.55m })).StatusCode);
        await JsonAsync(await _commissie.PutAsJsonAsync($"/api/v1/admin/parade-registrations/{a}/measured-length", new { measuredLengthMeters = 14m }), HttpStatusCode.NoContent);

        var detail = await _commissie.GetFromJsonAsync<JsonElement>($"/api/v1/admin/parade-registrations/{a}");
        Assert.Equal((12.5m, 14m), (detail.GetProperty("estimatedLengthMeters").GetDecimal(), detail.GetProperty("measuredLengthMeters").GetDecimal()));

        var missing = await _commissie.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-registrations?missing=MeasuredLength");
        Assert.Equal("De Bouwers", Assert.Single(missing.GetProperty("items").EnumerateArray()).GetProperty("groupName").GetString());
        var sorted = await _commissie.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-registrations?sort=Length&descending=true");
        Assert.Equal(["De Knotwilgen", "De Bouwers"], sorted.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("groupName").GetString()));
        Assert.Equal(2, (await _commissie.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-registrations?ageGroup=Adult")).GetProperty("totalCount").GetInt32());

        var summary = await _commissie.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-registrations/summary");
        Assert.Equal((2, 2, 16), (summary.GetProperty("active").GetInt32(), summary.GetProperty("approved").GetInt32(), summary.GetProperty("participants").GetInt32()));
        // 14 (gemeten) + 6 (geschat) + 2 × 5 m tussenruimte.
        Assert.Equal(30m, summary.GetProperty("lineupLengthMeters").GetDecimal());
        Assert.Equal(2, summary.GetProperty("categories").GetArrayLength());
    }
}
