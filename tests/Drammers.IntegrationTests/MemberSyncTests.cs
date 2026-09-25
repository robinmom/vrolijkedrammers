using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.EBoekhouden;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Import.Sync;
using Drammers.Modules.Membership.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 8: ledensync met e-Boekhouden (ADR-010) en ledenbeheer, met een e-Boekhouden in het geheugen.</summary>
[Collection(SqlServerCollection.Name)]
public class MemberSyncTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly FakeEBoekhouden _eb = new();
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), configure: services => services.AddSingleton<IEBoekhoudenClient>(_eb));
        var (_, oid) = await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur);
        _admin = _api.ClientFor(oid);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    /// <summary>Vraagt een run aan via de API en voert hem uit zoals de worker dat doet.</summary>
    private async Task<SyncJob> SyncAsync(bool dryRun = false)
    {
        var response = await _admin.PostAsync($"/api/v1/admin/members/import?dryRun={dryRun.ToString().ToLowerInvariant()}", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemberSync>().RunAsync(id, CancellationToken.None);
        return await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().SyncJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private Task<Member> MemberAsync(string number) => WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.MemberNumber == number));

    private void AddMembers(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            _eb.Add($"{i:000}", $"Piet van der Lid{i}", $"lid{i}@example.com");
        }
    }

    [Fact]
    public async Task Dry_run_rapporteert_maar_schrijft_geen_leden()
    {
        AddMembers(3);

        var job = await SyncAsync(dryRun: true);

        Assert.Equal(SyncJobStatus.Succeeded, job.Status);
        Assert.Equal((3, 3), (job.TotalInSource, job.Created));
        Assert.Equal(0, await WithDbAsync(db => db.Members.CountAsync()));
        Assert.Equal(3, await WithDbAsync(db => db.SyncJobItems.CountAsync(i => i.SyncJobId == job.Id && i.Action == SyncItemAction.Created)));
        Assert.Equal(_eb.OpenSessions, _eb.ClosedSessions);
    }

    [Fact]
    public async Task Tweede_run_zonder_wijzigingen_is_nul_nieuw_en_nul_gewijzigd()
    {
        AddMembers(4);
        var first = await SyncAsync();
        var second = await SyncAsync();

        Assert.Equal(4, first.Created);
        Assert.Equal((0, 0, 4), (second.Created, second.Updated, second.Unchanged));
        var member = await MemberAsync("001");
        Assert.Equal(("Piet", "van der", "Lid1"), (member.FirstName, member.NamePrefix, member.LastName));
        Assert.Equal(MembershipStatus.Active, member.MembershipStatus);
    }

    [Fact]
    public async Task Wijziging_in_e_Boekhouden_raakt_lokale_velden_niet_aan()
    {
        AddMembers(1);
        await SyncAsync();
        var id = (await MemberAsync("001")).Id;
        var patch = await _admin.PatchAsJsonAsync($"/api/v1/admin/members/{id}", new
        {
            localStatusOverride = "Suspended",
            membershipValidFrom = "2026-01-01",
            membershipValidTo = (string?)null,
            firstName = "Pieter",
            namePrefix = "van der",
            lastName = "Lid1",
            birthDate = "2000-05-01",
            joinYear = (short)2010,
        });
        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        _eb.Update("001", m => m with { EmailAddress = "nieuw@example.com", City = "Didam", Name = "Piet van der Lid" });
        var job = await SyncAsync();

        Assert.Equal(1, job.Updated);
        var item = await WithDbAsync(db => db.SyncJobItems.SingleAsync(i => i.SyncJobId == job.Id));
        Assert.Equal("name,city,email", item.ChangedFields);
        var member = await MemberAsync("001");
        Assert.Equal(("nieuw@example.com", "Didam"), (member.Email, member.City));
        Assert.Equal(MembershipStatus.Suspended, member.LocalStatusOverride);
        Assert.Equal("Pieter", member.FirstName); // handmatige naamcorrectie blijft
        Assert.Equal((new DateOnly(2000, 5, 1), (short?)2010), (member.BirthDate, member.JoinYear));
    }

    [Fact]
    public async Task Verdwenen_lid_wordt_eerst_missing_en_daarna_inactief_en_komt_terug_als_het_weer_verschijnt()
    {
        AddMembers(20);
        await SyncAsync();
        var lid = await _api.CreateUserAsync("lid005@example.com", DefaultRoles.Lid);
        await WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == lid.UserId);
            user.MemberId = (await db.Members.SingleAsync(m => m.MemberNumber == "005")).Id;
            return await db.SaveChangesAsync();
        });

        _eb.Remove("005");
        var first = await SyncAsync();
        Assert.Equal(1, first.Missing);
        Assert.Equal((MemberSyncState.Missing, MembershipStatus.Active), ((await MemberAsync("005")).SyncState, (await MemberAsync("005")).MembershipStatus));

        var second = await SyncAsync();
        Assert.Equal(1, second.Deactivated);
        Assert.Equal(MembershipStatus.Inactive, (await MemberAsync("005")).MembershipStatus);
        // Rollen en account blijven bewaard.
        Assert.Equal(1, await WithDbAsync(db => db.UserRoles.CountAsync(r => r.UserId == lid.UserId)));

        _eb.Add("005", "Piet van der Lid5", "lid5@example.com");
        var third = await SyncAsync();
        Assert.Equal(1, third.Reactivated);
        var back = await MemberAsync("005");
        Assert.Equal((MemberSyncState.InSync, MembershipStatus.Active, (DateTime?)null), (back.SyncState, back.MembershipStatus, back.EbMissingSince));
    }

    [Fact]
    public async Task Massadeletie_guard_deactiveert_niemand_als_meer_dan_10_procent_ontbreekt()
    {
        AddMembers(20);
        await SyncAsync();
        foreach (var number in new[] { "001", "002", "003" })
        {
            _eb.Remove(number);
        }

        var job = await SyncAsync();

        Assert.Equal(SyncJobStatus.Conflict, job.Status);
        Assert.Equal(0, job.Missing + job.Deactivated);
        Assert.Equal(0, await WithDbAsync(db => db.Members.CountAsync(m => m.SyncState == MemberSyncState.Missing)));
        var conflict = await WithDbAsync(db => db.SyncConflicts.SingleAsync());
        Assert.Equal(SyncConflictType.MassDeletionGuard, conflict.Type);
    }

    [Fact]
    public async Task Dubbel_lidnummer_en_e_mailwijziging_bij_actief_account_worden_conflicten()
    {
        AddMembers(2);
        await SyncAsync();
        var (userId, _) = await _api.CreateUserAsync("lid1@example.com", DefaultRoles.Lid);
        await WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = (await db.Members.SingleAsync(m => m.MemberNumber == "001")).Id;
            return await db.SaveChangesAsync();
        });

        _eb.Update("001", m => m with { EmailAddress = "ander@example.com" });
        _eb.Add("002", "Dubbel Lidnummer");
        var job = await SyncAsync();

        var types = await WithDbAsync(db => db.SyncConflicts.Select(c => c.Type).OrderBy(t => t).ToListAsync());
        Assert.Equal([SyncConflictType.DuplicateMemberNumber, SyncConflictType.EmailChangedForActiveAccount], types);
        Assert.Equal("ander@example.com", (await MemberAsync("001")).Email);
        Assert.Equal("lid1@example.com", (await WithDbAsync(db => db.Users.SingleAsync(u => u.Id == userId))).Email);
        Assert.Equal(SyncJobStatus.Conflict, job.Status);

        var conflicts = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/sync-conflicts");
        var first = conflicts.EnumerateArray().First().GetProperty("id").GetGuid();
        var resolve = await _admin.PostAsJsonAsync($"/api/v1/admin/sync-conflicts/{first}/resolve", new { resolution = "Accepted", note = "Klopt" });
        Assert.Equal(HttpStatusCode.NoContent, resolve.StatusCode);
        Assert.Single((await _admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/sync-conflicts")).EnumerateArray());
    }

    [Fact]
    public async Task Vrije_velden_volgens_mapping_en_parsefout_behoudt_de_oude_waarde()
    {
        var mapping = await _admin.PutAsJsonAsync("/api/v1/admin/config/member-mapping", new
        {
            birthDate = "freeText1",
            joinYear = "freeText2",
            status = "freeText3",
            category = (string?)null,
            inactiveStatusValues = new[] { "Opgezegd" },
        });
        Assert.Equal(HttpStatusCode.NoContent, mapping.StatusCode);
        _eb.Add("100", "Jan Jansen", freeText1: "1980-02-29", freeText2: "1995", freeText3: "actief");
        _eb.Add("101", "Kees de Opgezegde", freeText1: "01-03-1970", freeText2: "2001", freeText3: "opgezegd");
        await SyncAsync();

        var jan = await MemberAsync("100");
        Assert.Equal((new DateOnly(1980, 2, 29), (short?)1995, MembershipStatus.Active), (jan.BirthDate, jan.JoinYear, jan.MembershipStatus));
        Assert.Equal((new DateOnly(1970, 3, 1), MembershipStatus.Inactive), ((await MemberAsync("101")).BirthDate, (await MemberAsync("101")).MembershipStatus));

        _eb.Update("100", m => m with { FreeText1 = "geen datum" });
        var job = await SyncAsync();
        Assert.Equal((SyncJobStatus.SucceededWithWarnings, 1), (job.Status, job.Warnings));
        Assert.Equal(new DateOnly(1980, 2, 29), (await MemberAsync("100")).BirthDate);

        // Gemapte velden zijn in het portal niet te wijzigen.
        var patch = await _admin.PatchAsJsonAsync($"/api/v1/admin/members/{jan.Id}", new { birthDate = "1981-01-01", joinYear = (short)1995 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, patch.StatusCode);
    }

    [Fact]
    public async Task Mislukte_aanmelding_bij_e_Boekhouden_geeft_een_mislukte_run_met_melding()
    {
        _eb.FailToOpen = true;

        var job = await SyncAsync();

        Assert.Equal(SyncJobStatus.Failed, job.Status);
        Assert.Contains("API-token", job.ErrorMessage);
    }

    [Fact]
    public async Task Maximaal_een_run_tegelijk()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await _admin.PostAsync("/api/v1/admin/members/import", null)).StatusCode);

        var second = await _admin.PostAsync("/api/v1/admin/members/import", null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Ledenlijst_zoeken_detail_en_export_met_audit()
    {
        AddMembers(3);
        await SyncAsync();

        var page = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/members?search=Lid2");
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        var id = page.GetProperty("items")[0].GetProperty("id").GetGuid();
        var detail = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/members/{id}");
        Assert.Equal("002", detail.GetProperty("memberNumber").GetString());

        var export = await _admin.GetAsync("/api/v1/admin/members/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.Content.Headers.ContentType!.MediaType);
        Assert.Equal(1, await WithDbAsync(db => db.AuditLog.CountAsync(a => a.Action == "member.exported")));
    }

    [Fact]
    public async Task Leden_verwijderen_ontkoppelt_accounts_en_zet_de_nachtelijke_sync_uit()
    {
        AddMembers(3);
        await SyncAsync();
        var (userId, _) = await _api.CreateUserAsync("lid1@example.com", DefaultRoles.Lid);
        await WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = (await db.Members.FirstAsync()).Id;
            db.FeatureFlags.Add(new Drammers.Infrastructure.Configuration.FeatureFlag { Key = MemberSyncSettings.ScheduleFlag, Enabled = true });
            return await db.SaveChangesAsync();
        });

        var wrong = await _admin.PostAsJsonAsync("/api/v1/admin/members/purge", new { confirmation = "ja" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);

        var response = await _admin.PostAsJsonAsync("/api/v1/admin/members/purge", new { confirmation = MemberAdministration.PurgeConfirmation });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((3, 1, 1), (result.GetProperty("members").GetInt32(), result.GetProperty("syncJobs").GetInt32(), result.GetProperty("unlinkedAccounts").GetInt32()));
        Assert.Equal(0, await WithDbAsync(db => db.Members.CountAsync()));
        Assert.Null((await WithDbAsync(db => db.Users.SingleAsync(u => u.Id == userId))).MemberId);
        Assert.False(await WithDbAsync(db => db.FeatureFlags.Where(f => f.Key == MemberSyncSettings.ScheduleFlag).Select(f => f.Enabled).SingleAsync()));
        Assert.Equal(1, await WithDbAsync(db => db.AuditLog.CountAsync(a => a.Action == "member.purged")));
    }

    [Fact]
    public async Task Leden_verwijderen_kan_niet_in_productie()
    {
        await using var production = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(),
            configure: services => services.PostConfigure<MemberDataOptions>(o => o.AllowPurge = false));
        var (_, oid) = await production.CreateUserAsync("it@example.com", DefaultRoles.BeheerderIt);

        var response = await production.ClientFor(oid).PostAsJsonAsync("/api/v1/admin/members/purge", new { confirmation = MemberAdministration.PurgeConfirmation });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Redactie_mag_geen_leden_zien_of_verwijderen()
    {
        var (_, oid) = await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie);
        var redactie = _api.ClientFor(oid);

        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.PostAsJsonAsync("/api/v1/admin/members/purge", new { confirmation = MemberAdministration.PurgeConfirmation })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.PostAsync("/api/v1/admin/members/import", null)).StatusCode);
    }
}
