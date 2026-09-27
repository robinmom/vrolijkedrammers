using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Identity.AccountRequests;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Applications;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 9b-2: AVG-verzoeken in het portal (export, wissen) en het nachtelijk opruimen volgens de bewaartermijnen.</summary>
[Collection(SqlServerCollection.Name)]
public class PrivacyAndRetentionTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private static MembershipApplication Application(string email, ApplicationStatus status, DateTime createdAt, DateTime? decisionAt = null) => new()
    {
        Id = IdGenerator.NewId(),
        Status = status,
        FirstName = "Piet",
        LastName = "Test",
        BirthDate = new DateOnly(1990, 1, 1),
        AddressLine = "Straat 1",
        PostalCode = "6999 AA",
        City = "Loil",
        Email = email,
        MandateReference = $"DVD-{Guid.NewGuid():N}"[..24],
        CreatedAt = createdAt,
        DecisionAt = decisionAt,
        Iban = "NL91ABNA0417164300",
    };

    [Fact]
    public async Task Bestuur_exporteert_namens_een_lid_en_ziet_het_in_het_overzicht()
    {
        var (userId, _) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);

        var export = await (await _bestuur.PostAsync($"/api/v1/admin/users/{userId}/privacy-export", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(export.GetProperty("downloadUrl").GetString());

        var list = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/privacy-requests");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(("Export", "lid@example.com"), (item.GetProperty("type").GetString(), item.GetProperty("subjectName").GetString()));
        Assert.Equal("bestuur@example.com", item.GetProperty("requestedByBoard").GetString());
        Assert.Equal(HttpStatusCode.OK, (await _bestuur.GetAsync($"/api/v1/admin/privacy-requests/{export.GetProperty("id").GetGuid()}/download")).StatusCode);

        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/privacy-requests")).StatusCode);
    }

    [Fact]
    public async Task Wissen_verwijdert_account_inlog_en_alle_app_gegevens_maar_bewaart_de_auditlog()
    {
        var (userId, objectId) = await _api.CreateUserAsync("wis@example.com", DefaultRoles.Lid);
        await WithDbAsync(async db =>
        {
            db.MembershipApplications.Add(Application("wis@example.com", ApplicationStatus.Activated, DateTime.UtcNow));
            db.AccountRequests.Add(new AccountRequest { Id = IdGenerator.NewId(), MemberNumber = "1", Email = "wis@example.com", RequestedAt = DateTime.UtcNow, Status = AccountRequestStatus.Approved });
            db.LoginHistory.Add(new LoginHistory { UserId = userId, OccurredAt = DateTime.UtcNow, Result = LoginResult.Success });
            return await db.SaveChangesAsync();
        });

        var wrong = await _bestuur.PostAsJsonAsync($"/api/v1/admin/users/{userId}/erase", new { confirmation = "ja" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        var erase = await _bestuur.PostAsJsonAsync($"/api/v1/admin/users/{userId}/erase", new { confirmation = "WISSEN" });
        Assert.Equal(HttpStatusCode.OK, erase.StatusCode);
        var result = await erase.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((1, 1, 1), (result.GetProperty("applications").GetInt32(), result.GetProperty("accountRequests").GetInt32(), result.GetProperty("logins").GetInt32()));

        Assert.Contains(objectId, _api.Entra.Deleted);
        var user = await WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));
        Assert.Equal(AccountStatus.Deleted, user.AccountStatus);
        Assert.False(await WithDbAsync(db => db.MembershipApplications.AnyAsync(a => a.Email == "wis@example.com")));
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "privacy.erased" && a.EntityId == userId.ToString())));
        Assert.True(await WithDbAsync(db => db.PrivacyRequests.AnyAsync(r => r.UserId == userId && r.Type == Modules.Membership.Privacy.PrivacyRequestType.Erasure)));
    }

    [Fact]
    public async Task Laatste_beheerder_wissen_wordt_geweigerd_en_er_wordt_niets_gewist()
    {
        var bestuurId = await WithDbAsync(db => db.Users.Where(u => u.Email == "bestuur@example.com").Select(u => u.Id).SingleAsync());
        await WithDbAsync(async db =>
        {
            db.MembershipApplications.Add(Application("bestuur@example.com", ApplicationStatus.Activated, DateTime.UtcNow));
            return await db.SaveChangesAsync();
        });

        var erase = await _bestuur.PostAsJsonAsync($"/api/v1/admin/users/{bestuurId}/erase", new { confirmation = "WISSEN" });

        Assert.Equal(HttpStatusCode.Conflict, erase.StatusCode);
        Assert.True(await WithDbAsync(db => db.MembershipApplications.AnyAsync(a => a.Email == "bestuur@example.com")));
    }

    [Fact]
    public async Task Nachtelijk_opruimen_volgens_de_bewaartermijnen()
    {
        var now = _api.Clock.UtcNow.UtcDateTime;
        await WithDbAsync(async db =>
        {
            db.MembershipApplications.AddRange(
                Application("concept-oud@example.com", ApplicationStatus.Draft, now.AddDays(-8)),
                Application("concept-nieuw@example.com", ApplicationStatus.Draft, now.AddDays(-1)),
                Application("afgewezen-oud@example.com", ApplicationStatus.Rejected, now.AddDays(-200), now.AddDays(-190)),
                Application("afgewezen-nieuw@example.com", ApplicationStatus.Rejected, now.AddDays(-20), now.AddDays(-10)),
                Application("wacht@example.com", ApplicationStatus.Submitted, now.AddDays(-300)));
            db.AccountRequests.AddRange(
                new AccountRequest { Id = IdGenerator.NewId(), MemberNumber = "1", Email = "a@example.com", RequestedAt = now.AddDays(-100), DecidedAt = now.AddDays(-100), Status = AccountRequestStatus.Rejected },
                new AccountRequest { Id = IdGenerator.NewId(), MemberNumber = "2", Email = "b@example.com", RequestedAt = now.AddDays(-100), Status = AccountRequestStatus.Pending });
            db.LoginHistory.AddRange(
                new LoginHistory { OccurredAt = now.AddDays(-400), Result = LoginResult.Failed },
                new LoginHistory { OccurredAt = now.AddDays(-10), Result = LoginResult.Failed });
            return await db.SaveChangesAsync();
        });

        using var scope = _api.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<DataRetentionJob>().RunAsync(default);

        Assert.Equal((1, 1, 1, 1), (result.DraftApplications, result.RejectedApplications, result.AccountRequests, result.LoginHistory));
        var left = await WithDbAsync(db => db.MembershipApplications.Select(a => a.Email).OrderBy(e => e).ToListAsync());
        Assert.Equal(["afgewezen-nieuw@example.com", "concept-nieuw@example.com", "wacht@example.com"], left);
        Assert.Equal(AccountRequestStatus.Pending, (await WithDbAsync(db => db.AccountRequests.SingleAsync())).Status);
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "retention.completed")));
    }
}
