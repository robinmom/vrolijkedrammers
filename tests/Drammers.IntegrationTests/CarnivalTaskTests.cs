using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Takenlijst carnaval (2026-10-15): status en voortgang, kopiëren naar volgend jaar, rapportage en rechten.</summary>
[Collection(SqlServerCollection.Name)]
public class CarnivalTaskTests(SqlServerFixture sql) : IAsyncLifetime
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

    private async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddTaskAsync(int yearId, Guid categoryId, string title, string status, int progress, Guid[] members, string? dueOn = null) =>
        await CreatedIdAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/tasks?yearId={yearId}",
            new { categoryId, title, status, progress, dueOn, memberIds = members }));

    private static JsonElement Find(JsonElement year, string title) =>
        year.GetProperty("tasks").EnumerateArray().Single(t => t.GetProperty("title").GetString() == title);

    [Fact]
    public async Task Status_voortgang_kopieren_en_rapportage()
    {
        var femke = await AddMemberAsync("10", "Femke Peters");
        var jan = await AddMemberAsync("11", "Jan Jansen");
        var pronkzitting = await CreatedIdAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/categories", new { name = "Pronkzitting", sortOrder = 1 }));
        var optocht = await CreatedIdAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/categories", new { name = "Optocht", sortOrder = 2 }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/categories", new { name = "Optocht", sortOrder = 3 })).StatusCode);

        var yearId = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks")).GetProperty("yearId").GetInt32();
        var zaal = await AddTaskAsync(yearId, pronkzitting, "Zaal reserveren", "Done", 0, [femke], "2026-10-01");
        var artiesten = await AddTaskAsync(yearId, pronkzitting, "Artiesten boeken", "InProgress", 40, [femke, jan]);
        await AddTaskAsync(yearId, optocht, "Vergunning aanvragen", "NotStarted", 80, [jan], "2000-01-01");
        await AddTaskAsync(yearId, optocht, "Oude route", "Cancelled", 0, []);

        var year = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        // Afgerond: 100% en vandaag gestart en klaar; nog niet gestart: voortgang terug naar 0 en geen startdatum.
        Assert.Equal((100, today, today), (Find(year, "Zaal reserveren").GetProperty("progress").GetInt32(),
            Find(year, "Zaal reserveren").GetProperty("startedOn").GetString(), Find(year, "Zaal reserveren").GetProperty("completedOn").GetString()));
        Assert.Equal(today, Find(year, "Artiesten boeken").GetProperty("startedOn").GetString());
        Assert.Equal(0, Find(year, "Vergunning aanvragen").GetProperty("progress").GetInt32());
        Assert.Equal(JsonValueKind.Null, Find(year, "Vergunning aanvragen").GetProperty("startedOn").ValueKind);
        Assert.True(Find(year, "Vergunning aanvragen").GetProperty("overdue").GetBoolean());

        // (100 + 40 + 0) / 3, geannuleerd telt niet mee.
        var summary = year.GetProperty("summary");
        Assert.Equal((4, 1, 1, 1, 1, 1, 47), (summary.GetProperty("total").GetInt32(), summary.GetProperty("done").GetInt32(),
            summary.GetProperty("inProgress").GetInt32(), summary.GetProperty("notStarted").GetInt32(), summary.GetProperty("cancelled").GetInt32(),
            summary.GetProperty("overdue").GetInt32(), summary.GetProperty("percent").GetInt32()));

        // Bijwerken naar 75% houdt de oorspronkelijke startdatum; Jan gaat eraf.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/tasks/{artiesten}",
            new { categoryId = pronkzitting, title = "Artiesten boeken", status = "InProgress", progress = 75, memberIds = new[] { femke } })).StatusCode);
        var updated = Find(await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks"), "Artiesten boeken");
        Assert.Equal((75, today), (updated.GetProperty("progress").GetInt32(), updated.GetProperty("startedOn").GetString()));
        Assert.Equal("Femke Peters", Assert.Single(updated.GetProperty("assignees").EnumerateArray()).GetProperty("name").GetString());

        // Rapportage per categorie en per persoon.
        var report = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks/report");
        var perCategory = report.GetProperty("perCategory").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("percent").GetInt32());
        Assert.Equal(88, perCategory["Pronkzitting"]);
        Assert.Equal(0, perCategory["Optocht"]);
        var perPerson = report.GetProperty("perPerson").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("total").GetInt32());
        Assert.Equal(2, perPerson["Femke Peters"]);
        Assert.Equal(1, perPerson["Jan Jansen"]);

        // Kopiëren kan pas als het volgende carnavalsjaar er is.
        Assert.Equal(HttpStatusCode.Conflict, (await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/copy", new { fromYearId = yearId, taskIds = new[] { zaal } })).StatusCode);
        var nextYearId = await WithDbAsync(async db =>
        {
            var next = new CarnivalYear
            {
                Name = "2027/2028",
                StartDate = new DateOnly(2027, 11, 11),
                EndDate = new DateOnly(2028, 3, 1),
                CarnivalStartDate = new DateOnly(2028, 2, 26),
                CarnivalEndDate = new DateOnly(2028, 2, 29),
            };
            db.CarnivalYears.Add(next);
            await db.SaveChangesAsync();
            return next.Id;
        });

        var copy = new { fromYearId = yearId, taskIds = new[] { zaal, artiesten } };
        Assert.Equal(2, (await (await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/copy", copy)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("copied").GetInt32());
        Assert.Equal(0, (await (await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/copy", copy)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("copied").GetInt32());

        var next = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/tasks?yearId={nextYearId}");
        Assert.Equal(2, next.GetProperty("tasks").GetArrayLength());
        var copied = Find(next, "Zaal reserveren");
        Assert.Equal(("NotStarted", 0, "2027-10-01", JsonValueKind.Null), (copied.GetProperty("status").GetString(), copied.GetProperty("progress").GetInt32(),
            copied.GetProperty("dueOn").GetString(), copied.GetProperty("startedOn").ValueKind));
        Assert.Equal("Femke Peters", copied.GetProperty("assignees")[0].GetProperty("name").GetString());

        // Een categorie met taken kan niet weg.
        Assert.Equal(HttpStatusCode.Conflict, (await _bestuur.DeleteAsync($"/api/v1/admin/tasks/categories/{optocht}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.DeleteAsync($"/api/v1/admin/tasks/{zaal}")).StatusCode);
    }

    [Fact]
    public async Task Festivallijst_apart_met_import_uit_het_draaiboek()
    {
        var (_, oid) = await _api.CreateUserAsync("chantal@example.com", DefaultRoles.Dansgardefestival);
        var chantal = _api.ClientFor(oid);
        Assert.Equal(HttpStatusCode.Forbidden, (await chantal.GetAsync("/api/v1/admin/tasks")).StatusCode);

        // Een categorie van carnaval is niet te zien of te gebruiken in de festivallijst.
        var carnaval = await CreatedIdAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/tasks/categories", new { name = "Pronkzitting", sortOrder = 1 }));
        var yearId = (await chantal.GetFromJsonAsync<JsonElement>("/api/v1/admin/festival/tasks")).GetProperty("yearId").GetInt32();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await chantal.PostAsJsonAsync($"/api/v1/admin/festival/tasks?yearId={yearId}",
            new { categoryId = carnaval, title = "x", status = "NotStarted", progress = 0 })).StatusCode);

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Actiepunten");
        string?[][] rows =
        [
            ["Actiepuntenlijst Dansgardefestival De Vrolijke Drammers", null, null, null],
            ["Actie", "Wie", "Plan", "opmerkingen"],
            ["Eerste voorbereidingen November 2025", null, null, null],
            ["Geluid/ muziek regelen", "Naomie/Twan/Rick", null, "Twan organiseert dit."],
            ["Jury vragen", "Miranda", null, "Merel, Demi, Tonny"],
            ["Op de dag zelf", null, null, null],
            ["wc's bijhouden", "Vrijwilligers", null, null],
        ];
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                if (rows[r][c] is { } v)
                {
                    sheet.Cell(r + 1, c + 1).Value = v;
                }
            }
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        HttpResponseMessage Import()
        {
            var file = new ByteArrayContent(stream.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            var content = new MultipartFormDataContent { { file, "file", "draaiboek.xlsx" } };
            return chantal.PostAsync($"/api/v1/admin/festival/tasks/import?yearId={yearId}", content).GetAwaiter().GetResult();
        }

        var result = await Import().Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((2, 3, 0), (result.GetProperty("categories").GetInt32(), result.GetProperty("tasks").GetInt32(), result.GetProperty("skipped").GetInt32()));
        Assert.Equal(3, (await Import().Content.ReadFromJsonAsync<JsonElement>()).GetProperty("skipped").GetInt32());

        var categories = (await chantal.GetFromJsonAsync<JsonElement>("/api/v1/admin/festival/tasks/categories")).EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["Eerste voorbereidingen November", "Op de dag zelf"], categories);
        var list = await chantal.GetFromJsonAsync<JsonElement>("/api/v1/admin/festival/tasks");
        var geluid = list.GetProperty("tasks").EnumerateArray().Single(t => t.GetProperty("title").GetString() == "Geluid/ muziek regelen");
        Assert.Equal(("Naomie/Twan/Rick", "Twan organiseert dit."), (geluid.GetProperty("otherPeople").GetString(), geluid.GetProperty("description").GetString()));

        // De carnavalslijst blijft leeg; per persoon telt elke losse naam.
        Assert.Equal(0, (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tasks")).GetProperty("tasks").GetArrayLength());
        var people = (await chantal.GetFromJsonAsync<JsonElement>("/api/v1/admin/festival/tasks/report")).GetProperty("perPerson").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()!).Order().ToArray();
        Assert.Equal(["Miranda", "Naomie", "Rick", "Twan", "Vrijwilligers"], people);
    }

    [Fact]
    public async Task Festivalorganisatie_zoekt_leden_alleen_op_naam_met_minimaal_drie_tekens()
    {
        await AddMemberAsync("1234", "Anna de Vries");
        var (_, oid) = await _api.CreateUserAsync("chantal@example.com", DefaultRoles.Dansgardefestival);
        var festival = _api.ClientFor(oid);

        async Task<string[]> SearchAsync(HttpClient client, string list, string search) =>
            [.. (await client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/{list}/members?search={Uri.EscapeDataString(search)}"))
                .EnumerateArray().Select(m => m.GetProperty("name").GetString()!)];

        Assert.Equal(["Anna de Vries"], await SearchAsync(festival, "festival/tasks", "Anna"));
        Assert.Empty(await SearchAsync(festival, "festival/tasks", "An"));
        Assert.Empty(await SearchAsync(festival, "festival/tasks", "1234"));
        var found = (await festival.GetFromJsonAsync<JsonElement>("/api/v1/admin/festival/tasks/members?search=Anna"))[0];
        Assert.Equal(["memberId", "name"], found.EnumerateObject().Select(p => p.Name).Order().ToArray());

        // De takenlijst van carnaval (task.manage) zoekt zoals voorheen, ook op lidnummer.
        Assert.Equal(["Anna de Vries"], await SearchAsync(_bestuur, "tasks", "An"));
        Assert.Equal(["Anna de Vries"], await SearchAsync(_bestuur, "tasks", "1234"));
    }

    [Fact]
    public async Task Alleen_met_het_recht_takenlijst()
    {
        var (_, oid) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(oid).GetAsync("/api/v1/admin/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _bestuur.GetAsync("/api/v1/admin/tasks")).StatusCode);
    }
}
