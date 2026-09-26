using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 8c: groepen en individuele leden als doelgroep (REQ-EVT-02) en de basisrapportage.</summary>
[Collection(SqlServerCollection.Name)]
public class GroupAudienceTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        var (_, oid) = await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur);
        _bestuur = _api.ClientFor(oid);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    /// <summary>Maakt een lid en een app-account (rol Lid) dat eraan gekoppeld is; geeft lid-id en client terug.</summary>
    private async Task<(Guid MemberId, HttpClient Client)> MemberWithAccountAsync(
        string number, DateOnly? birthDate = null, short? joinYear = null, MembershipStatus status = MembershipStatus.Active)
    {
        var (userId, oid) = await _api.CreateUserAsync($"lid{number}@example.com", DefaultRoles.Lid);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var member = new Member
        {
            Id = IdGenerator.NewId(),
            MemberNumber = number,
            FullName = $"Lid {number}",
            MembershipStatus = status,
            BirthDate = birthDate,
            JoinYear = joinYear,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = member.Id;
        await db.SaveChangesAsync();
        return (member.Id, _api.ClientFor(oid));
    }

    private async Task<Guid> CreateGroupAsync(string name)
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/groups", new { name, description = (string?)null, type = "Committee", carnivalYearId = (int?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateRestrictedEventAsync(string title, Guid[]? groups = null, Guid[]? members = null)
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/events", new
        {
            categoryId = 1,
            title,
            summary = (string?)null,
            description = (string?)null,
            startAt = _api.Clock.UtcNow.AddDays(5),
            endAt = (DateTimeOffset?)null,
            allDay = false,
            locationName = (string?)null,
            locationAddress = (string?)null,
            latitude = (decimal?)null,
            longitude = (decimal?)null,
            isHighlight = false,
            badgeText = (string?)null,
            publication = new { visibility = "Restricted", audienceRoles = Array.Empty<string>(), status = "Published", publishAt = (DateTimeOffset?)null, audienceGroups = groups ?? [], audienceMembers = members ?? [] },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<List<string>> TitlesAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonElement>("/api/v1/events")).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!)];

    [Fact]
    public async Task Event_voor_groep_Jeugdcommissie_is_alleen_zichtbaar_voor_leden_van_die_groep()
    {
        var group = await CreateGroupAsync("Jeugdcommissie");
        var (inGroup, inGroupClient) = await MemberWithAccountAsync("100");
        var (_, otherClient) = await MemberWithAccountAsync("101");
        Assert.Equal(HttpStatusCode.NoContent,
            (await _bestuur.PutAsJsonAsync($"/api/v1/admin/groups/{group}/members/{inGroup}", new { function = "Member", validFrom = (string?)null, validTo = (string?)null })).StatusCode);
        var eventId = await CreateRestrictedEventAsync("Vergadering jeugdcommissie", groups: [group]);

        Assert.Contains("Vergadering jeugdcommissie", await TitlesAsync(inGroupClient));
        Assert.DoesNotContain("Vergadering jeugdcommissie", await TitlesAsync(otherClient));
        Assert.DoesNotContain("Vergadering jeugdcommissie", await TitlesAsync(_api.CreateClient()));
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/v1/events/{eventId}")).StatusCode);

        // Het beheer toont de groep als doelgroep.
        var admin = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/events/{eventId}");
        Assert.Equal(group, admin.GetProperty("publication").GetProperty("audienceGroups")[0].GetGuid());
    }

    [Fact]
    public async Task Verlopen_groepslidmaatschap_en_geschorst_lid_zien_de_groepscontent_niet()
    {
        var group = await CreateGroupAsync("Dansgarde leiding");
        var (expired, expiredClient) = await MemberWithAccountAsync("200");
        var (suspended, suspendedClient) = await MemberWithAccountAsync("201");
        var yesterday = DateOnly.FromDateTime(_api.Clock.UtcNow.UtcDateTime).AddDays(-1);
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/groups/{group}/members/{expired}", new { function = "Lead", validFrom = (string?)null, validTo = yesterday.ToString("yyyy-MM-dd") });
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/groups/{group}/members/{suspended}", new { function = "Member", validFrom = (string?)null, validTo = (string?)null });
        await _bestuur.PatchAsJsonAsync($"/api/v1/admin/members/{suspended}", new { localStatusOverride = "Suspended" });
        await CreateRestrictedEventAsync("Training leiding", groups: [group]);

        Assert.DoesNotContain("Training leiding", await TitlesAsync(expiredClient));
        Assert.DoesNotContain("Training leiding", await TitlesAsync(suspendedClient));
    }

    [Fact]
    public async Task Event_voor_een_individueel_lid_is_alleen_voor_dat_lid()
    {
        var (target, targetClient) = await MemberWithAccountAsync("300");
        var (_, otherClient) = await MemberWithAccountAsync("301");
        await CreateRestrictedEventAsync("Persoonlijke uitnodiging", members: [target]);

        Assert.Contains("Persoonlijke uitnodiging", await TitlesAsync(targetClient));
        Assert.DoesNotContain("Persoonlijke uitnodiging", await TitlesAsync(otherClient));
    }

    [Fact]
    public async Task Beperkt_zonder_rol_groep_of_lid_wordt_geweigerd()
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/news", new
        {
            title = "Geheim",
            summary = (string?)null,
            body = "tekst",
            category = (string?)null,
            expireAt = (DateTimeOffset?)null,
            publication = new { visibility = "Restricted", audienceRoles = Array.Empty<string>(), status = "Published", audienceGroups = Array.Empty<Guid>() },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Groepen_beheren_dubbele_naam_en_rechten()
    {
        var group = await CreateGroupAsync("Optochtgroep De Drammers");
        var duplicate = await _bestuur.PostAsJsonAsync("/api/v1/admin/groups", new { name = "Optochtgroep De Drammers", type = "ParadeGroup" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var (member, _) = await MemberWithAccountAsync("400");
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/groups/{group}/members/{member}", new { function = "Lead" });
        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/groups/{group}");
        Assert.Equal("Lead", detail.GetProperty("members")[0].GetProperty("function").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.DeleteAsync($"/api/v1/admin/groups/{group}/members/{member}")).StatusCode);

        // Redactie mag groepen kiezen als doelgroep, maar geen leden of groepsleden zien.
        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        var options = await redactie.GetFromJsonAsync<JsonElement>("/api/v1/admin/content-audiences/groups");
        Assert.Equal("Optochtgroep De Drammers", options[0].GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync($"/api/v1/admin/groups/{group}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.DeleteAsync($"/api/v1/admin/groups/{group}")).StatusCode);
        Assert.Equal(0, (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/groups")).GetArrayLength());
    }

    [Fact]
    public async Task Rapportage_telt_per_status_leeftijd_inschrijfjaar_en_groep()
    {
        var today = DateOnly.FromDateTime(_api.Clock.UtcNow.UtcDateTime);
        var group = await CreateGroupAsync("Raad van Elf");
        var (child, _) = await MemberWithAccountAsync("500", birthDate: today.AddYears(-10), joinYear: 2020);
        await MemberWithAccountAsync("501", birthDate: today.AddYears(-30), joinYear: 2020);
        await MemberWithAccountAsync("502", joinYear: 1990);
        await MemberWithAccountAsync("503", status: MembershipStatus.Inactive);
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/groups/{group}/members/{child}", new { function = "Member" });

        var report = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports/members");

        Assert.Equal((4, 3), (report.GetProperty("total").GetInt32(), report.GetProperty("active").GetInt32()));
        int Count(string list, string label) =>
            report.GetProperty(list).EnumerateArray().Single(r => r.GetProperty("label").GetString() == label).GetProperty("count").GetInt32();
        Assert.Equal((3, 1), (Count("byStatus", "Actief"), Count("byStatus", "Inactief")));
        Assert.Equal((1, 1, 1), (Count("byAgeClass", "0–11"), Count("byAgeClass", "25–39"), Count("byAgeClass", "Onbekend")));
        Assert.Equal((2, 1), (Count("byJoinYear", "2020"), Count("byJoinYear", "1990")));
        Assert.Equal(1, Count("byGroup", "Raad van Elf"));
        Assert.Equal(3, Count("byRole", "Carnavalist"));

        var export = await _bestuur.GetAsync("/api/v1/admin/reports/members/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);

        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie2@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/reports/members")).StatusCode);
    }

    [Fact]
    public async Task Ledensamenvatting_en_groepen_op_het_lid_detail()
    {
        var group = await CreateGroupAsync("Jeugdcommissie");
        var (active, _) = await MemberWithAccountAsync("600");
        await MemberWithAccountAsync("601", status: MembershipStatus.Inactive);
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/groups/{group}/members/{active}", new { function = "Lead" });

        var summary = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/members/summary");
        Assert.Equal((1, 1, 1), (summary.GetProperty("active").GetInt32(), summary.GetProperty("inactive").GetInt32(), summary.GetProperty("activeWithAccount").GetInt32()));
        Assert.Equal(0, summary.GetProperty("missingInEBoekhouden").GetInt32());

        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/members/{active}");
        var groups = detail.GetProperty("groups");
        Assert.Equal(("Jeugdcommissie", "Lead"), (groups[0].GetProperty("name").GetString(), groups[0].GetProperty("function").GetString()));
    }
}
