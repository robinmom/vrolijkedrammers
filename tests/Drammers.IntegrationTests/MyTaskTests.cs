using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.Website;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>
/// De takenlijst carnaval in de app (2026-11-12): een lid ziet alleen zijn eigen taken, zet de stand bij en voegt er
/// zelf een toe. Alleen het kader komt erin, plus wie het bestuur op een taak heeft gezet.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MyTaskTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private Task<Guid> AddMemberAsync(string number, string name) =>
        WithDbAsync(async db =>
        {
            var member = new Member { Id = IdGenerator.NewId(), MemberNumber = number, FullName = name, MembershipStatus = MembershipStatus.Active };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            return member.Id;
        });

    /// <summary>Een ingelogd lid: een account met een rol, gekoppeld aan het lid.</summary>
    private async Task<HttpClient> SignInAsync(string email, Guid memberId, params string[] roles)
    {
        var (userId, objectId) = await _api.CreateUserAsync(email, roles);
        await WithDbAsync(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.MemberId, memberId)));
        return _api.ClientFor(objectId);
    }

    /// <summary>Op de kaderlijst van de website zetten; dat is dezelfde lijst die de adverteerders gebruiken.</summary>
    private Task OnCommitteeAsync(Guid memberId, string name) =>
        WithDbAsync(async db =>
        {
            var committee = await db.Committees.FirstOrDefaultAsync();
            if (committee is null)
            {
                committee = new Committee { Name = "Bestuur", Slug = "bestuur" };
                db.Committees.Add(committee);
                await db.SaveChangesAsync();
            }

            db.CommitteeMembers.Add(new CommitteeMember
            {
                Id = IdGenerator.NewId(),
                CommitteeId = committee.Id,
                MemberId = memberId,
                Name = name,
                Function = "Bestuurslid",
            });
            return await db.SaveChangesAsync();
        });

    private async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddTaskAsync(int yearId, Guid categoryId, string title, string status, int progress, Guid[] members, string? dueOn = null) =>
        await CreatedIdAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/tasks?yearId={yearId}",
            new { categoryId, title, status, progress, dueOn, memberIds = members }));

    private static JsonElement Task(JsonElement mine, string title) =>
        mine.GetProperty("items").EnumerateArray().Single(t => t.GetProperty("title").GetString() == title);

    [Fact]
    public async Task Een_lid_ziet_alleen_zijn_eigen_taken_en_zet_de_stand_bij()
    {
        var ferry = await AddMemberAsync("20", "Ferry Stienissen");
        var ander = await AddMemberAsync("21", "Iemand Anders");
        await OnCommitteeAsync(ferry, "Ferry Stienissen");
        var app = await SignInAsync("ferry@example.com", ferry, DefaultRoles.Lid);

        var carnaval = await CreatedIdAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/categories", new { name = "Carnaval", sortOrder = 1 }));
        var yearId = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks")).GetProperty("yearId").GetInt32();
        var bands = await AddTaskAsync(yearId, carnaval, "Bands en Dj's", "InProgress", 25, [ferry, ander]);
        var klaar = await AddTaskAsync(yearId, carnaval, "Zaal regelen", "Done", 100, [ferry]);
        var vanAnder = await AddTaskAsync(yearId, carnaval, "Niet van Ferry", "NotStarted", 0, [ander]);

        var mine = await app.GetFromJsonAsync<JsonElement>("/api/v1/me/tasks");
        Assert.True(mine.GetProperty("allowed").GetBoolean());
        Assert.Equal(2, mine.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("Niet van Ferry", mine.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("title").GetString()));
        Assert.Equal(1, mine.GetProperty("summary").GetProperty("done").GetInt32());
        Assert.Equal(2, mine.GetProperty("summary").GetProperty("total").GetInt32());
        // Gemiddelde voortgang van 25% en 100% = 62,5; Math.Round rondt naar even af, net als het portal (Summarize).
        Assert.Equal(62, mine.GetProperty("summary").GetProperty("percent").GetInt32());
        // Wie er nog meer op staat, zonder het lid zelf.
        Assert.Equal("Samen met Iemand Anders", Task(mine, "Bands en Dj's").GetProperty("people").GetString());

        // De stand bijwerken, met een opmerking.
        Assert.Equal(HttpStatusCode.NoContent,
            (await app.PutAsJsonAsync($"/api/v1/me/tasks/{bands}", new { status = "InProgress", progress = 75, note = "Zondag is rond." })).StatusCode);
        mine = await app.GetFromJsonAsync<JsonElement>("/api/v1/me/tasks");
        Assert.Equal(75, Task(mine, "Bands en Dj's").GetProperty("progress").GetInt32());
        Assert.Equal("Zondag is rond.", Task(mine, "Bands en Dj's").GetProperty("note").GetString());
        // Het bestuur leest diezelfde opmerking in het portal.
        var year = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks");
        Assert.Equal("Zondag is rond.",
            year.GetProperty("tasks").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == bands).GetProperty("note").GetString());

        // De taak van een ander is niet van hem; dat is geen 403 maar gewoon "niet gevonden".
        Assert.Equal(HttpStatusCode.NotFound,
            (await app.PutAsJsonAsync($"/api/v1/me/tasks/{vanAnder}", new { status = "Done", progress = 100, note = (string?)null })).StatusCode);

        // "Niet van toepassing" haalt hem van de lijst: hij blijft staan, maar telt nergens meer in mee.
        Assert.Equal(HttpStatusCode.NoContent,
            (await app.PutAsJsonAsync($"/api/v1/me/tasks/{klaar}", new { status = "Cancelled", progress = 0, note = (string?)null })).StatusCode);
        mine = await app.GetFromJsonAsync<JsonElement>("/api/v1/me/tasks");
        Assert.Equal("Cancelled", Task(mine, "Zaal regelen").GetProperty("status").GetString());
        Assert.Equal(1, mine.GetProperty("summary").GetProperty("total").GetInt32());
        Assert.Equal(0, mine.GetProperty("summary").GetProperty("done").GetInt32());
    }

    [Fact]
    public async Task Een_gewoon_lid_komt_er_niet_in_maar_wie_een_taak_krijgt_wel()
    {
        var gewoon = await AddMemberAsync("30", "Gewoon Lid");
        var helper = await AddMemberAsync("31", "Vrijwillige Helper");
        var gewoonApp = await SignInAsync("gewoon@example.com", gewoon, DefaultRoles.Lid);
        var helperApp = await SignInAsync("helper@example.com", helper, DefaultRoles.Lid);

        var categorie = await CreatedIdAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/categories", new { name = "Algemeen", sortOrder = 1 }));
        var yearId = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks")).GetProperty("yearId").GetInt32();

        // Zonder kaderlijst, zonder rol en zonder taak: de app laat de knop niet zien.
        var leeg = await gewoonApp.GetFromJsonAsync<JsonElement>("/api/v1/me/tasks");
        Assert.False(leeg.GetProperty("allowed").GetBoolean());
        Assert.Empty(leeg.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await gewoonApp.PostAsJsonAsync("/api/v1/me/tasks", new { categoryId = categorie, title = "Mag niet" })).StatusCode);

        // Zet het bestuur hem wél op een taak, dan hoort hij hem te zien (besluit 2026-11-12).
        await AddTaskAsync(yearId, categorie, "Zaal versieren", "NotStarted", 0, [helper]);
        var vanHelper = await helperApp.GetFromJsonAsync<JsonElement>("/api/v1/me/tasks");
        Assert.True(vanHelper.GetProperty("allowed").GetBoolean());
        Assert.Equal("Zaal versieren", Task(vanHelper, "Zaal versieren").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Een_kaderlid_zet_zelf_een_taak_op_de_lijst()
    {
        var kaderlid = await AddMemberAsync("40", "Kader Lid");
        await OnCommitteeAsync(kaderlid, "Kader Lid");
        var app = await SignInAsync("kader@example.com", kaderlid, DefaultRoles.Lid);

        var optocht = await CreatedIdAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/categories", new { name = "Optocht", sortOrder = 1 }));
        var mine = await app.GetFromJsonAsync<JsonElement>("/api/v1/me/tasks");
        Assert.Equal("Optocht", mine.GetProperty("categories").EnumerateArray().Single().GetProperty("name").GetString());

        var id = await CreatedIdAsync(await app.PostAsJsonAsync("/api/v1/me/tasks",
            new { categoryId = optocht, title = "Kaartverkoop regelen", description = "Bij de Jumbo", dueOn = "2027-02-13" }));

        // Hij staat op zijn eigen naam in de lijst van het bestuur.
        var year = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks");
        var portal = year.GetProperty("tasks").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);
        Assert.Equal("Kader Lid", portal.GetProperty("assignees").EnumerateArray().Single().GetProperty("name").GetString());
        Assert.Equal("Optocht", portal.GetProperty("categoryName").GetString());
        Assert.Equal("2027-02-13", portal.GetProperty("dueOn").GetString());

        // Een categorie die niet bestaat gaat niet door.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await app.PostAsJsonAsync("/api/v1/me/tasks", new { categoryId = Guid.NewGuid(), title = "Onzin" })).StatusCode);
    }
}
