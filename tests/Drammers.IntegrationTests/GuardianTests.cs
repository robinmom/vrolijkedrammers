using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Identity.AccountRequests;
using Drammers.Modules.Membership.Groups;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>
/// Fase 17: ouders/verzorgers (koppelen, voorstellen bij hetzelfde e-mailadres, koppelverzoeken, eigen account vanaf 15)
/// en de dansgarde (vrij veld "groep" = Dansgarde, dansgroepen, doelgroep voor meldingen).
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class GuardianTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private DateOnly Today => DateOnly.FromDateTime(_api.Clock.UtcNow.UtcDateTime);

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Accepted ? default : await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<Guid> MemberAsync(string number, string first, string last, int age, string? email, string? group = null) =>
        WithDbAsync(async db =>
        {
            var member = new Member
            {
                Id = IdGenerator.NewId(),
                MemberNumber = number,
                FullName = $"{first} {last}",
                FirstName = first,
                LastName = last,
                Email = email,
                BirthDate = Today.AddYears(-age).AddDays(-30),
                ParadeGroupName = group,
                MembershipStatus = MembershipStatus.Active,
            };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            return member.Id;
        });

    /// <summary>Een lid met een app-account (rol Lid).</summary>
    private async Task<(Guid MemberId, Guid UserId, HttpClient Client)> MemberWithAccountAsync(string number, string first, string last, int age, string email)
    {
        var memberId = await MemberAsync(number, first, last, age, email);
        var (userId, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid);
        await WithDbAsync(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.MemberId, memberId).SetProperty(u => u.DisplayName, $"{first} {last}")));
        return (memberId, userId, _api.ClientFor(oid));
    }

    [Fact]
    public async Task Voorstel_bij_zelfde_email_pas_na_bevestiging_gekoppeld_en_zichtbaar_bij_mijn_kinderen()
    {
        var (_, robinUserId, robin) = await MemberWithAccountAsync("1003", "Robin", "Mom", 44, "fam.mom@example.com");
        var lot = await MemberAsync("1042", "Lot", "Mom", 9, "fam.mom@example.com", group: "dansgarde");
        await MemberAsync("1043", "Fenna", "Mom", 7, "fam.mom@example.com");
        var anouk = await MemberAsync("1012", "Anouk", "Bakker", 39, "bakker@example.com");
        var tess = await MemberAsync("1090", "Tess", "Bakker", 11, "bakker@example.com");

        // Voorstellen: kinderen onder 15 met een ander lid op hetzelfde adres; broertjes/zusjes onder 15 onderling niet.
        var suggestions = (await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/guardian-suggestions"))).EnumerateArray().ToList();
        Assert.Equal(3, suggestions.Count);
        var lotSuggestion = suggestions.Single(s => s.GetProperty("childMemberId").GetGuid() == lot);
        Assert.Equal("Robin Mom", lotSuggestion.GetProperty("parentName").GetString());
        Assert.True(lotSuggestion.GetProperty("childDansgarde").GetBoolean());
        Assert.Equal(robinUserId, lotSuggestion.GetProperty("parentUserId").GetGuid());

        // Niets gebeurt vanzelf.
        Assert.Equal(0, await WithDbAsync(db => db.GuardianRelations.CountAsync()));
        Assert.Empty((await JsonAsync(await robin.GetAsync("/api/v1/me/children"))).EnumerateArray());

        await JsonAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/guardian-suggestions/link", new { childMemberId = lot, parentMemberId = await WithDbAsync(db => db.Members.Where(m => m.MemberNumber == "1003").Select(m => m.Id).SingleAsync()) }), HttpStatusCode.NoContent);
        var children = (await JsonAsync(await robin.GetAsync("/api/v1/me/children"))).EnumerateArray().ToList();
        var child = Assert.Single(children);
        Assert.Equal("Lot Mom", child.GetProperty("fullName").GetString());
        Assert.True(child.GetProperty("canShowQr").GetBoolean());
        Assert.False(child.GetProperty("ownAccount").GetBoolean());

        // "Geen relatie" onthoudt de keuze.
        await JsonAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/guardian-suggestions/dismiss", new { childMemberId = tess, parentMemberId = anouk }), HttpStatusCode.NoContent);
        var left = (await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/guardian-suggestions"))).EnumerateArray().ToList();
        Assert.Equal("Fenna Mom", Assert.Single(left).GetProperty("childName").GetString());

        // De ouder heeft nu de rol Ouder en kreeg een melding.
        var ouderRole = await WithDbAsync(db => db.Roles.Where(r => r.Code == DefaultRoles.Ouder).Select(r => r.Id).SingleAsync());
        Assert.True(await WithDbAsync(db => db.Users.AnyAsync(u => u.Id == robinUserId && u.Roles.Any(r => r.RoleId == ouderRole))));
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "guardian.linked")));
    }

    [Fact]
    public async Task Voorstel_zonder_account_maakt_account_voor_de_ouder()
    {
        var anouk = await MemberAsync("1012", "Anouk", "Bakker", 39, "bakker@example.com");
        var tess = await MemberAsync("1090", "Tess", "Bakker", 11, "bakker@example.com");

        await JsonAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/guardian-suggestions/link", new { childMemberId = tess, parentMemberId = anouk }), HttpStatusCode.NoContent);

        var user = await WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == "bakker@example.com"));
        Assert.Equal(anouk, user.MemberId);
        Assert.Contains(_api.Emails.Sent, m => m.To == "bakker@example.com" && m.Subject.StartsWith("Je account", StringComparison.Ordinal));
        var parent = _api.ClientFor(_api.Entra.SignUp("bakker@example.com"));
        Assert.Equal("Tess Bakker", Assert.Single((await JsonAsync(await parent.GetAsync("/api/v1/me/children"))).EnumerateArray()).GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Hooguit_twee_ouders_alleen_onder_18_en_uitnodigen_en_ontkoppelen()
    {
        var lot = await MemberAsync("1042", "Lot", "Mom", 9, null);
        var adult = await MemberAsync("1050", "Kees", "Groot", 19, null);
        var (_, robinUserId, _) = await MemberWithAccountAsync("1003", "Robin", "Mom", 44, "robin@example.com");

        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{lot}/guardians", new { userId = robinUserId, relationship = "Parent" }), HttpStatusCode.NoContent);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{lot}/guardians", new { userId = robinUserId, relationship = "Parent" }), HttpStatusCode.Conflict);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{lot}/guardians/invite", new { email = "sanne@example.com", name = "Sanne Mom", relationship = "Caregiver" }), HttpStatusCode.NoContent);
        Assert.Contains(_api.Emails.Sent, m => m.To == "sanne@example.com");
        var third = await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{lot}/guardians/invite", new { email = "opa@example.com", name = "Opa Mom", relationship = "Caregiver" }), HttpStatusCode.Conflict);
        Assert.Equal("GUARDIAN_LIMIT", third.GetProperty("code").GetString());
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{adult}/guardians", new { userId = robinUserId, relationship = "Parent" }), HttpStatusCode.Conflict);

        var card = await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/members/{lot}/guardians"));
        Assert.True(card.GetProperty("applies").GetBoolean());
        var guardians = card.GetProperty("guardians").EnumerateArray().OrderBy(g => g.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Robin Mom", "Sanne Mom"], guardians.Select(g => g.GetProperty("name").GetString()));
        Assert.Equal("Caregiver", guardians[1].GetProperty("relationship").GetString());
        Assert.False(card.GetProperty("ownAccount").GetProperty("canGetOwnAccount").GetBoolean());

        await JsonAsync(await _bestuur.DeleteAsync($"/api/v1/admin/members/{lot}/guardians/{guardians[1].GetProperty("id").GetGuid()}"), HttpStatusCode.NoContent);
        Assert.Equal(1, await WithDbAsync(db => db.GuardianRelations.CountAsync(g => g.MemberId == lot)));

        // Alleen-lezen rollen mogen niet koppelen.
        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.PostAsJsonAsync($"/api/v1/admin/members/{lot}/guardians", new { userId = robinUserId, relationship = "Parent" })).StatusCode);
    }

    [Fact]
    public async Task Koppelverzoek_uit_de_app_wordt_pas_na_goedkeuring_actief()
    {
        var lot = await MemberAsync("1042", "Lot", "Mom", 9, null);
        await MemberAsync("1077", "Fenna", "Janssen", 9, null);
        await MemberAsync("1102", "Fenna", "Janssen", 12, null);
        var (parentUserId, parentOid) = await _api.CreateUserAsync("sanne@example.com", DefaultRoles.Lid);
        var parent = _api.ClientFor(parentOid);

        var created = await JsonAsync(await parent.PostAsJsonAsync("/api/v1/me/guardian-requests", new { childFirstName = "lot", childLastName = "mom", relationship = "Parent", phone = "0698765432" }), HttpStatusCode.Created);
        await JsonAsync(await parent.PostAsJsonAsync("/api/v1/me/guardian-requests", new { childFirstName = "Fenna", childLastName = "Janssen", relationship = "Caregiver" }), HttpStatusCode.Created);
        await JsonAsync(await parent.PostAsJsonAsync("/api/v1/me/guardian-requests", new { childFirstName = "Fenna", childLastName = "Janssen", relationship = "Caregiver" }), HttpStatusCode.Conflict);
        Assert.Empty((await JsonAsync(await parent.GetAsync("/api/v1/me/children"))).EnumerateArray());

        var requests = (await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/guardian-requests?status=Pending"))).EnumerateArray().ToList();
        Assert.Equal(2, requests.Count);
        var lotRequest = requests.Single(r => r.GetProperty("childFirstName").GetString() == "lot");
        Assert.Equal(lot, Assert.Single(lotRequest.GetProperty("candidates").EnumerateArray()).GetProperty("memberId").GetGuid());
        var fenna = requests.Single(r => r.GetProperty("childFirstName").GetString() == "Fenna");
        Assert.Equal(2, fenna.GetProperty("candidates").GetArrayLength());

        // Het verzoek staat ook bij het lid in het portal.
        Assert.Single((await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/members/{lot}/guardians"))).GetProperty("requests").EnumerateArray());

        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/guardian-requests/{created.GetProperty("id").GetGuid()}/approve", new { memberId = lot }), HttpStatusCode.NoContent);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/guardian-requests/{fenna.GetProperty("id").GetGuid()}/reject", new { reason = "Onbekend" }), HttpStatusCode.NoContent);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/guardian-requests/{fenna.GetProperty("id").GetGuid()}/reject", new { reason = "Nogmaals" }), HttpStatusCode.Conflict);

        Assert.Equal("Lot Mom", Assert.Single((await JsonAsync(await parent.GetAsync("/api/v1/me/children"))).EnumerateArray()).GetProperty("fullName").GetString());
        var mine = (await JsonAsync(await parent.GetAsync("/api/v1/me/guardian-requests"))).EnumerateArray().Select(r => r.GetProperty("status").GetString()).Order().ToList();
        Assert.Equal(["Approved", "Rejected"], mine);
        var relation = await WithDbAsync(db => db.GuardianRelations.AsNoTracking().SingleAsync(g => g.MemberId == lot));
        Assert.Equal((parentUserId, "0698765432"), (relation.GuardianUserId, relation.GuardianPhone));
        Assert.Equal(2, await WithDbAsync(db => db.Notifications.CountAsync(n => n.SourceType == "GuardianRequest")));
    }

    [Fact]
    public async Task Eigen_account_vanaf_15_de_ouder_houdt_meldingen_maar_niet_de_QR()
    {
        var sem = await MemberAsync("1060", "Sem", "Mom", 14, "fam.mom@example.com");
        var (_, robinUserId, robin) = await MemberWithAccountAsync("1003", "Robin", "Mom", 44, "fam.mom@example.com");
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{sem}/guardians", new { userId = robinUserId, relationship = "Parent" }), HttpStatusCode.NoContent);

        var tooYoung = await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{sem}/own-account", new { email = "sem@example.com" }), HttpStatusCode.Conflict);
        Assert.Equal("OWN_ACCOUNT_NOT_ALLOWED", tooYoung.GetProperty("code").GetString());

        await WithDbAsync(db => db.Members.Where(m => m.Id == sem).ExecuteUpdateAsync(s => s.SetProperty(m => m.BirthDate, Today.AddYears(-15).AddDays(-1))));
        var inUse = await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{sem}/own-account", new { email = "fam.mom@example.com" }), HttpStatusCode.Conflict);
        Assert.Equal("EMAIL_IN_USE", inUse.GetProperty("code").GetString());
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{sem}/own-account", new { email = "Sem@Example.com" }), HttpStatusCode.Accepted);
        Assert.True((await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/members/{sem}/guardians"))).GetProperty("ownAccount").GetProperty("pending").GetBoolean());

        var messages = await WithDbAsync(db => db.Outbox.AsNoTracking().Where(m => m.Type == MemberAccounts.ProvisionMessageType).ToListAsync());
        foreach (var message in messages)
        {
            using var scope = _api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MemberAccounts>().RunProvisioningAsync(
                JsonSerializer.Deserialize<MemberAccounts.ProvisionMessage>(message.Payload, JsonSerializerOptions.Web)!, CancellationToken.None);
        }

        var semUser = await WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.MemberId == sem));
        Assert.Equal("sem@example.com", semUser.Email);
        Assert.Contains(_api.Emails.Sent, m => m.To == "sem@example.com" && m.Subject.StartsWith("Je account", StringComparison.Ordinal));
        var child = Assert.Single((await JsonAsync(await robin.GetAsync("/api/v1/me/children"))).EnumerateArray());
        Assert.True(child.GetProperty("ownAccount").GetBoolean());
        Assert.False(child.GetProperty("canShowQr").GetBoolean());
        var own = (await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/members/{sem}/guardians"))).GetProperty("ownAccount");
        Assert.Equal("sem@example.com", own.GetProperty("email").GetString());

        // Meldingen aan Sem gaan naar Sem zelf én namens Sem naar Robin; na 18 niet meer naar Robin.
        using (var scope = _api.Services.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<Drammers.Infrastructure.Notifications.NotificationAudienceResolver>();
            var recipients = await resolver.ResolveAsync(new NotificationAudience(MemberIds: [sem]), CancellationToken.None);
            Assert.Contains(recipients, r => r.UserId == semUser.Id && r.OnBehalfOfMemberId is null);
            Assert.Contains(recipients, r => r.UserId == robinUserId && r.OnBehalfOfMemberId == sem);
        }

        await WithDbAsync(db => db.Members.Where(m => m.Id == sem).ExecuteUpdateAsync(s => s.SetProperty(m => m.BirthDate, Today.AddYears(-18).AddDays(-1))));
        using (var scope = _api.Services.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<Drammers.Infrastructure.Notifications.NotificationAudienceResolver>();
            var recipients = await resolver.ResolveAsync(new NotificationAudience(MemberIds: [sem]), CancellationToken.None);
            Assert.DoesNotContain(recipients, r => r.UserId == robinUserId);
        }

        Assert.Empty((await JsonAsync(await robin.GetAsync("/api/v1/me/children"))).EnumerateArray());
    }

    [Fact]
    public async Task Accountverzoek_voor_een_kind_onder_15_wacht_op_het_bestuur()
    {
        await MemberAsync("1042", "Lot", "Mom", 9, "fam.mom@example.com");

        var response = await _api.CreateClient().PostAsJsonAsync("/api/v1/account-requests", new { memberNumber = "1042", email = "fam.mom@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var request = await WithDbAsync(db => db.AccountRequests.AsNoTracking().SingleAsync());
        Assert.Equal((AccountRequestStatus.Pending, "minor"), (request.Status, request.MismatchReason));
        Assert.False(await WithDbAsync(db => db.Outbox.AnyAsync(m => m.Type == MemberAccounts.ProvisionMessageType)));
    }

    [Fact]
    public async Task Dansgarde_overzicht_dansgroepen_indelen_en_melding_met_ouders()
    {
        var lot = await MemberAsync("1042", "Lot", "Mom", 9, null, group: "dansgarde ");
        var noor = await MemberAsync("1101", "Noor", "Smit", 14, null, group: "Dansgarde");
        var piet = await MemberAsync("1001", "Piet", "Lid", 40, null, group: "Boerenbruiloft");
        var (_, robinUserId, _) = await MemberWithAccountAsync("1003", "Robin", "Mom", 44, "robin@example.com");
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{lot}/guardians", new { userId = robinUserId, relationship = "Parent" }), HttpStatusCode.NoContent);

        var mini = await JsonAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/groups", new { name = "Mini Drammers", description = "5 – 9 jaar", type = "DanceGuard", active = true }), HttpStatusCode.Created);
        var miniId = mini.GetProperty("id").GetGuid();

        await JsonAsync(await _bestuur.PutAsJsonAsync($"/api/v1/admin/dansgarde/{lot}/group", new { groupId = miniId }), HttpStatusCode.NoContent);
        var notDansgarde = await JsonAsync(await _bestuur.PutAsJsonAsync($"/api/v1/admin/dansgarde/{piet}/group", new { groupId = miniId }), HttpStatusCode.UnprocessableEntity);
        Assert.Equal("NOT_DANSGARDE", notDansgarde.GetProperty("code").GetString());

        var overview = await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/dansgarde"));
        Assert.Equal((2, 1, 1), (overview.GetProperty("total").GetInt32(), overview.GetProperty("withoutGroup").GetInt32(), overview.GetProperty("withoutGuardian").GetInt32()));
        var lotRow = overview.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("memberId").GetGuid() == lot);
        Assert.Equal("Mini Drammers", lotRow.GetProperty("danceGroup").GetProperty("name").GetString());
        Assert.Equal(["Robin Mom"], lotRow.GetProperty("guardians").EnumerateArray().Select(g => g.GetString()));
        var noorRow = overview.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("memberId").GetGuid() == noor);
        Assert.Equal(JsonValueKind.Null, noorRow.GetProperty("danceGroup").ValueKind);

        var groups = await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/dansgarde/groups"));
        var group = Assert.Single(groups.GetProperty("groups").EnumerateArray());
        Assert.Equal(["Lot Mom"], group.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("fullName").GetString()));
        Assert.Equal(["Noor Smit"], groups.GetProperty("unassigned").EnumerateArray().Select(m => m.GetProperty("fullName").GetString()));

        // Uit de dansgroep halen.
        await JsonAsync(await _bestuur.PutAsJsonAsync($"/api/v1/admin/dansgarde/{lot}/group", new { groupId = (Guid?)null }), HttpStatusCode.NoContent);
        Assert.False(await WithDbAsync(db => db.GroupMemberships.AnyAsync(gm => gm.MemberId == lot)));

        // Melding aan de dansgarde: bereikt de ouder namens het kind.
        var preview = await JsonAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/notifications/preview-audience", new { audience = new { dansgarde = true }, category = "DanceGuard" }));
        Assert.Equal(1, preview.GetProperty("accounts").GetInt32());
        using var scope = _api.Services.CreateScope();
        var recipients = await scope.ServiceProvider.GetRequiredService<Drammers.Infrastructure.Notifications.NotificationAudienceResolver>()
            .ResolveAsync(new NotificationAudience(Dansgarde: true), CancellationToken.None);
        Assert.Equal(lot, Assert.Single(recipients).OnBehalfOfMemberId);
        Assert.Equal(GroupType.DanceGuard, await WithDbAsync(db => db.Groups.Where(g => g.Id == miniId).Select(g => g.Type).SingleAsync()));
    }
}
