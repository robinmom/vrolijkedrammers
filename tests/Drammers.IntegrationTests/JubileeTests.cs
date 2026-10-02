using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>
/// Fase 20: jubilarissen. Het actieve (geseede) carnavalsjaar is 2026/2027 met carnaval in februari 2027, dus 11 jaar =
/// inschrijfjaar 2016 en 22 jaar = 2005.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class JubileeTests(SqlServerFixture sql) : IAsyncLifetime
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

    private async Task<Guid> MemberAsync(string number, short? joinYear, MembershipStatus status = MembershipStatus.Active, Guid? userId = null)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var member = new Member
        {
            Id = IdGenerator.NewId(),
            MemberNumber = number,
            FullName = $"Lid {number}",
            LastName = $"Lid {number}",
            MembershipStatus = status,
            JoinYear = joinYear,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        if (userId is { } id)
        {
            (await db.Users.SingleAsync(u => u.Id == id)).MemberId = member.Id;
            await db.SaveChangesAsync();
        }

        return member.Id;
    }

    private static IReadOnlyList<(string Number, int Years)> Jubilarians(JsonElement report) =>
        [.. report.GetProperty("jubilarians").EnumerateArray()
            .Select(j => (j.GetProperty("memberNumber").GetString()!, j.GetProperty("years").GetInt32()))];

    [Fact]
    public async Task Jubilarissen_van_het_actieve_carnavalsjaar()
    {
        await MemberAsync("1", 2016);
        await MemberAsync("2", 2005);
        await MemberAsync("3", 2015);
        await MemberAsync("4", 2016, MembershipStatus.Inactive);
        await MemberAsync("5", 2016, MembershipStatus.Deceased);
        await MemberAsync("6", null);
        var corrected = await MemberAsync("7", 2010);

        var report = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/jubilees");
        Assert.Equal(2027, report.GetProperty("referenceYear").GetInt32());
        Assert.Equal([("2", 22), ("1", 11)], Jubilarians(report));
        Assert.Equal(["6"], report.GetProperty("withoutJoinYear").EnumerateArray().Select(m => m.GetProperty("memberNumber").GetString()));

        // Correctie: het jubileum van lid 7 telt vanaf 2016.
        var put = await _bestuur.PutAsJsonAsync($"/api/v1/admin/jubilees/members/{corrected}", new { joinYearOverride = 2016, note = "Tussen 2012 en 2017 geen lid" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        report = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/jubilees");
        var seven = report.GetProperty("jubilarians").EnumerateArray().Single(j => j.GetProperty("memberNumber").GetString() == "7");
        Assert.Equal((2010, 2016, 11), (seven.GetProperty("joinYear").GetInt32(), seven.GetProperty("baseYear").GetInt32(), seven.GetProperty("years").GetInt32()));
        Assert.Equal("Tussen 2012 en 2017 geen lid", seven.GetProperty("note").GetString());

        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/members/{corrected}");
        Assert.Equal(2016, detail.GetProperty("jubileeJoinYearOverride").GetInt32());

        var invalid = await _bestuur.PutAsJsonAsync($"/api/v1/admin/jubilees/members/{corrected}", new { joinYearOverride = 1800 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        var export = await _bestuur.GetAsync("/api/v1/admin/jubilees/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("jubilarissen-2026-2027.xlsx", export.Content.Headers.ContentDisposition?.FileNameStar ?? export.Content.Headers.ContentDisposition?.FileName);

        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/jubilees")).StatusCode);
    }

    [Fact]
    public async Task Ander_carnavalsjaar_en_eigen_jubilea()
    {
        int previousYear;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            var year = new CarnivalYear
            {
                Name = "2025/2026",
                StartDate = new DateOnly(2025, 11, 11),
                EndDate = new DateOnly(2026, 2, 18),
                CarnivalStartDate = new DateOnly(2026, 2, 14),
                CarnivalEndDate = new DateOnly(2026, 2, 17),
            };
            db.CarnivalYears.Add(year);
            await db.SaveChangesAsync();
            previousYear = year.Id;
        }

        await MemberAsync("1", 2015);
        await MemberAsync("2", 2004);
        await MemberAsync("3", 1993);
        await MemberAsync("4", 2001);

        // Voorbeeld van het bestuur: in 2026 waren 2015, 2004 en 1993 de jubileumjaren.
        var report = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/jubilees?carnivalYearId={previousYear}");
        Assert.Equal(2026, report.GetProperty("referenceYear").GetInt32());
        Assert.Equal([("3", 33), ("2", 22), ("1", 11)], Jubilarians(report));

        var put = await _bestuur.PutAsJsonAsync("/api/v1/admin/jubilees/settings", new { milestones = new[] { 25, 11 } });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var settings = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/jubilees/settings");
        Assert.Equal([11, 25], settings.GetProperty("milestones").EnumerateArray().Select(m => m.GetInt32()));
        report = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/jubilees?carnivalYearId={previousYear}");
        Assert.Equal([("4", 25), ("1", 11)], Jubilarians(report));

        var invalid = await _bestuur.PutAsJsonAsync("/api/v1/admin/jubilees/settings", new { milestones = Array.Empty<int>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
    }

    [Fact]
    public async Task Lid_ziet_aantal_jaren_en_jubileum_in_de_app()
    {
        var (jubilarisId, jubilarisOid) = await _api.CreateUserAsync("jubilaris@example.com", DefaultRoles.Lid);
        await MemberAsync("10", 2016, userId: jubilarisId);
        var (otherId, otherOid) = await _api.CreateUserAsync("ander@example.com", DefaultRoles.Lid);
        await MemberAsync("11", 2020, userId: otherId);

        var me = await _api.ClientFor(jubilarisOid).GetFromJsonAsync<JsonElement>("/api/v1/me/member");
        Assert.Equal((11, true), (me.GetProperty("yearsMember").GetInt32(), me.GetProperty("isJubilee").GetBoolean()));

        var other = await _api.ClientFor(otherOid).GetFromJsonAsync<JsonElement>("/api/v1/me/member");
        Assert.Equal((7, false), (other.GetProperty("yearsMember").GetInt32(), other.GetProperty("isJubilee").GetBoolean()));
    }
}
