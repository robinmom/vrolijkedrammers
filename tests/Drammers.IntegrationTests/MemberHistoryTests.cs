using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Applications;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Ledenverloop per carnavalsjaar en de openstaande aanmeldingen op het dashboard (2026-10-15).</summary>
[Collection(SqlServerCollection.Name)]
public class MemberHistoryTests(SqlServerFixture sql) : IAsyncLifetime
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

    private Task<int> AddMemberAsync(string number, short joinYear, MembershipStatus status = MembershipStatus.Active, MembershipKind? kind = null) =>
        WithDbAsync(db =>
        {
            db.Members.Add(new Member
            {
                Id = IdGenerator.NewId(),
                MemberNumber = number,
                FullName = $"Lid {number}",
                JoinYear = joinYear,
                MembershipStatus = status,
                MembershipKind = kind,
            });
            return db.SaveChangesAsync();
        });

    private async Task RecordAsync()
    {
        using var scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemberHistory>().RecordAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Uitschrijvingen_en_totaal_vastleggen_nieuwe_leden_zonder_gesplitste_en_handmatige_cijfers_gaan_voor()
    {
        var current = JoinYears.ForDate(DateOnly.FromDateTime(DateTime.UtcNow));
        await AddMemberAsync("1", (short)current);
        await AddMemberAsync("2", (short)current, kind: MembershipKind.Partner); // gesplitst: geen nieuw lid
        await AddMemberAsync("3", 2001);
        await AddMemberAsync("4", 2001);
        await RecordAsync();

        // Lid 4 wordt inactief, lid 3 overlijdt: twee uitschrijvingen in dit carnavalsjaar; het totaal daalt.
        await WithDbAsync(db => db.Members.Where(m => m.MemberNumber == "4").ExecuteUpdateAsync(s => s.SetProperty(m => m.MembershipStatus, MembershipStatus.Inactive)));
        await WithDbAsync(db => db.Members.Where(m => m.MemberNumber == "3").ExecuteUpdateAsync(s => s.SetProperty(m => m.LocalStatusOverride, MembershipStatus.Deceased)));
        await RecordAsync();
        await RecordAsync(); // niet dubbel

        var report = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports/members/history");
        var rows = report.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("year").GetInt32());
        Assert.Equal(1, rows[current].GetProperty("newMembers").GetInt32());
        Assert.Equal(2, rows[current].GetProperty("leftMembers").GetInt32());
        Assert.Equal(2, rows[current].GetProperty("totalMembers").GetInt32());
        Assert.Equal(2, rows[2001].GetProperty("newMembers").GetInt32());
        Assert.Equal(JsonValueKind.Null, rows[2001].GetProperty("leftMembers").ValueKind);
        Assert.Equal(2, await WithDbAsync(db => db.MemberExits.CountAsync()));

        // Weer actief in hetzelfde jaar: geen uitschrijving meer.
        await WithDbAsync(db => db.Members.Where(m => m.MemberNumber == "4").ExecuteUpdateAsync(s => s.SetProperty(m => m.MembershipStatus, MembershipStatus.Active)));
        await RecordAsync();
        Assert.Equal(1, await WithDbAsync(db => db.MemberExits.CountAsync()));

        // Oudere jaren met de hand: dat gaat voor.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync("/api/v1/admin/reports/members/history/2001",
            new { newMembers = 25, leftMembers = 7, totalMembers = 540 })).StatusCode);
        var manual = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports/members/history"))
            .GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("year").GetInt32() == 2001);
        Assert.Equal((25, 7, 540), (manual.GetProperty("newMembers").GetInt32(), manual.GetProperty("leftMembers").GetInt32(), manual.GetProperty("totalMembers").GetInt32()));
        Assert.True(manual.GetProperty("manual").GetProperty("totalMembers").GetBoolean());
    }

    [Fact]
    public async Task Historie_importeren_uit_Excel()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Leden");
        sheet.Cell(1, 1).Value = "Carnavalsjaar";
        sheet.Cell(1, 2).Value = "Totaal";
        sheet.Cell(1, 3).Value = "Nieuw";
        sheet.Cell(1, 4).Value = "Uitgeschreven";
        sheet.Cell(2, 1).Value = "2019/2020";
        sheet.Cell(2, 2).Value = 590;
        sheet.Cell(2, 3).Value = 31;
        sheet.Cell(2, 4).Value = 14;
        sheet.Cell(3, 1).Value = 2021;
        sheet.Cell(3, 2).Value = 600;
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        var file = new ByteArrayContent(stream.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using var content = new MultipartFormDataContent { { file, "file", "ledenverloop.xlsx" } };

        var result = await (await _bestuur.PostAsync("/api/v1/admin/reports/members/history/import", content)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetProperty("years").GetInt32());
        var stats = await WithDbAsync(db => db.MemberYearStats.AsNoTracking().OrderBy(s => s.CarnivalYear).ToListAsync());
        Assert.Equal(2, stats.Count);
        Assert.Equal((2020, 31, 14, 590), (stats[0].CarnivalYear, stats[0].ManualNew, stats[0].ManualLeft, stats[0].ManualTotal));
        Assert.Equal((2021, null, null, 600), (stats[1].CarnivalYear, stats[1].ManualNew, stats[1].ManualLeft, stats[1].ManualTotal));
    }

    [Fact]
    public async Task Dashboard_openstaande_aanmeldingen_per_recht()
    {
        await WithDbAsync(db =>
        {
            foreach (var (status, n) in new[] { (ApplicationStatus.Submitted, 2), (ApplicationStatus.InReview, 1), (ApplicationStatus.Activated, 3) })
            {
                for (var i = 0; i < n; i++)
                {
                    db.MembershipApplications.Add(new MembershipApplication
                    {
                        Id = IdGenerator.NewId(),
                        FirstName = "Piet",
                        LastName = "Test",
                        Email = $"{status}{i}@example.com",
                        AddressLine = "Dorpsstraat 1",
                        PostalCode = "6999 AA",
                        City = "Loil",
                        BirthDate = new DateOnly(1990, 1, 1),
                        Status = status,
                        CreatedAt = DateTime.UtcNow,
                        MandateReference = $"DVD-T{i}{status}",
                    });
                }
            }

            return db.SaveChangesAsync();
        });

        var pending = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/dashboard/pending");
        Assert.Equal((2, 1), (pending.GetProperty("members").GetProperty("new").GetInt32(), pending.GetProperty("members").GetProperty("inReview").GetInt32()));
        Assert.Equal(0, pending.GetProperty("parade").GetProperty("new").GetInt32());

        // Alleen de onderdelen waarvoor iemand rechten heeft.
        var (_, oid) = await _api.CreateUserAsync("chantal@example.com", DefaultRoles.Dansgardefestival);
        var chantal = await _api.ClientFor(oid).GetFromJsonAsync<JsonElement>("/api/v1/admin/dashboard/pending");
        Assert.Equal(JsonValueKind.Null, chantal.GetProperty("members").ValueKind);
        Assert.Equal(JsonValueKind.Null, chantal.GetProperty("parade").ValueKind);
        Assert.Equal(0, chantal.GetProperty("festival").GetProperty("new").GetInt32());
    }
}
