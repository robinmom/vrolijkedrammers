using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Inloggen met wachtwoord (2026-10-12): elk lid direct een account, welkomstmails, geen accountverzoeken meer.</summary>
[Collection(SqlServerCollection.Name)]
public class AccountWelcomeTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(
            await sql.CreateMigratedDatabaseAsync(), configure: services => services.Configure<AccountOptions>(o => o.PasswordSignIn = true));
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private Task<Guid> AddMemberAsync(string number, string? email, DateOnly? birthDate = null) =>
        WithDbAsync(async db =>
        {
            var member = new Member
            {
                Id = IdGenerator.NewId(),
                MemberNumber = number,
                FullName = $"Piet Lid{number}",
                FirstName = "Piet",
                Email = email,
                BirthDate = birthDate ?? new DateOnly(1980, 1, 1),
                MembershipStatus = MembershipStatus.Active,
            };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            return member.Id;
        });

    /// <summary>Verwerkt de openstaande provisioning- en welkomstberichten zoals de worker (ook de geplande).</summary>
    private async Task RunOutboxAsync()
    {
        string[] types = [MemberAccounts.ProvisionMessageType, AccountWelcomes.MessageType];
        var messages = await WithDbAsync(db => db.Outbox.AsNoTracking().Where(m => types.Contains(m.Type)).OrderBy(m => m.LockedUntil).ToListAsync());
        await WithDbAsync(db => db.Outbox.Where(m => types.Contains(m.Type)).ExecuteDeleteAsync());
        foreach (var message in messages)
        {
            using var scope = _api.Services.CreateScope();
            if (message.Type == AccountWelcomes.MessageType)
            {
                await scope.ServiceProvider.GetRequiredService<AccountWelcomes>().SendAsync(
                    JsonSerializer.Deserialize<AccountWelcomes.WelcomeMessage>(message.Payload, JsonSerializerOptions.Web)!, CancellationToken.None);
            }
            else
            {
                await scope.ServiceProvider.GetRequiredService<MemberAccounts>().RunProvisioningAsync(
                    JsonSerializer.Deserialize<MemberAccounts.ProvisionMessage>(message.Payload, JsonSerializerOptions.Web)!, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task Account_aanmaken_maakt_direct_een_inlog_met_wachtwoord_en_de_mail_legt_wachtwoord_vergeten_uit()
    {
        var memberId = await AddMemberAsync("0201", "Jan@Example.com");

        Assert.Equal(HttpStatusCode.Accepted, (await _bestuur.PostAsync($"/api/v1/admin/members/{memberId}/provision-account", null)).StatusCode);
        await RunOutboxAsync();

        var objectId = await WithDbAsync(db => db.Users.Where(u => u.MemberId == memberId).Select(u => u.ExternalObjectId).SingleAsync());
        Assert.False(PendingObjectId.IsPending(objectId));
        Assert.True(_api.Entra.PasswordSignIn.ContainsKey(objectId));
        Assert.Equal(objectId, _api.Entra.AccountsByEmail["jan@example.com"]);
        var mail = Assert.Single(_api.Emails.Sent);
        Assert.Contains("Wachtwoord vergeten?", mail.PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain("Maak er een", mail.PlainText, StringComparison.Ordinal);

        // De inlog die de API maakte, werkt meteen (geen koppeling bij de eerste aanmelding nodig).
        var me = await _api.ClientFor(objectId).GetFromJsonAsync<JsonElement>("/api/v1/me/member");
        Assert.Equal("0201", me.GetProperty("memberNumber").GetString());
    }

    [Fact]
    public async Task Accountverzoek_kan_niet_meer()
    {
        await AddMemberAsync("0202", "kees@example.com");

        var response = await _api.CreateClient().PostAsJsonAsync("/api/v1/account-requests", new { memberNumber = "0202", email = "kees@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(await WithDbAsync(db => db.AccountRequests.AnyAsync()));
    }

    [Fact]
    public async Task Welkomstmail_naar_iedereen_maakt_accounts_slaat_kinderen_en_gedeelde_adressen_over_en_stuurt_niemand_dubbel()
    {
        var volwassene = await AddMemberAsync("0301", "anna@example.com");
        var metWachtendAccount = await AddMemberAsync("0302", "bert@example.com");
        var pendingObjectId = PendingObjectId.New();
        await WithDbAsync(async db =>
        {
            db.Users.Add(new User
            {
                Id = IdGenerator.NewId(),
                ExternalObjectId = pendingObjectId,
                Email = "bert@example.com",
                DisplayName = "Bert",
                MemberId = metWachtendAccount,
                AccountStatus = AccountStatus.Active,
            });
            return await db.SaveChangesAsync();
        });
        await AddMemberAsync("0303", "kind@example.com", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-10));
        await AddMemberAsync("0304", null);
        await AddMemberAsync("0305", "Anna@Example.com"); // partner met hetzelfde adres
        await _api.CreateUserAsync("ouder@example.com", DefaultRoles.Ouder);

        var preview = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/account-welcomes/preview");
        Assert.Equal(2, preview.GetProperty("members").GetInt32());
        Assert.Equal(2, preview.GetProperty("otherAccounts").GetInt32()); // bestuur en ouder
        Assert.Equal(1, preview.GetProperty("children").GetInt32());
        Assert.Equal(1, preview.GetProperty("withoutEmail").GetInt32());
        Assert.Equal(1, preview.GetProperty("sharedEmail").GetInt32());
        Assert.True(preview.GetProperty("passwordSignIn").GetBoolean());

        var send = await _bestuur.PostAsync("/api/v1/admin/account-welcomes/send-all", null);
        Assert.Equal(HttpStatusCode.Accepted, send.StatusCode);
        Assert.Equal(4, (await send.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("queued").GetInt32());
        // Gespreid over de tijd (limiet van de mailservice).
        var planned = await WithDbAsync(db => db.Outbox.Where(m => m.Type == AccountWelcomes.MessageType).Select(m => m.LockedUntil!.Value).OrderBy(t => t).ToListAsync());
        Assert.True(planned[^1] - planned[0] >= AccountWelcomes.Interval * 3 - TimeSpan.FromSeconds(1));

        await RunOutboxAsync();

        Assert.Equal(
            ["anna@example.com", "bert@example.com", "bestuur@example.com", "ouder@example.com"],
            _api.Emails.Sent.Select(m => m.To).Order().ToArray());
        Assert.All(_api.Emails.Sent, m => Assert.Contains("Wachtwoord vergeten?", m.PlainText, StringComparison.Ordinal));
        var users = await WithDbAsync(db => db.Users.AsNoTracking().ToListAsync());
        Assert.All(users, u => Assert.NotNull(u.WelcomeSentAt));
        Assert.All(users, u => Assert.True(_api.Entra.PasswordSignIn.ContainsKey(u.ExternalObjectId)));
        // Het wachtende account kreeg de nieuwe inlog: geen tweede account.
        var bert = Assert.Single(users, u => u.MemberId == metWachtendAccount);
        Assert.False(PendingObjectId.IsPending(bert.ExternalObjectId));
        Assert.Single(users, u => u.MemberId == volwassene);

        var again = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/account-welcomes/preview");
        Assert.Equal(0, again.GetProperty("members").GetInt32() + again.GetProperty("otherAccounts").GetInt32());
        Assert.Equal(4, again.GetProperty("alreadySent").GetInt32());

        // Opnieuw sturen naar één lid (bijv. wachtwoord kwijt) kan altijd.
        Assert.Equal(HttpStatusCode.Accepted, (await _bestuur.PostAsync($"/api/v1/admin/members/{volwassene}/welcome", null)).StatusCode);
        await RunOutboxAsync();
        Assert.Equal(2, _api.Emails.Sent.Count(m => m.To == "anna@example.com"));
    }

    [Fact]
    public async Task Welkomstmail_eerst_aan_een_selectie_daarna_aan_de_rest()
    {
        var inGroep = await AddMemberAsync("0501", "groep@example.com");
        var hoofdlid = await AddMemberAsync("0502", "hoofd@example.com");
        var tweede = await AddMemberAsync("0503", "tweede@example.com");
        var los = await AddMemberAsync("0504", "los@example.com");
        await AddMemberAsync("0505", "rest@example.com");
        var groupId = IdGenerator.NewId();
        await WithDbAsync(async db =>
        {
            db.Groups.Add(new Drammers.Modules.Membership.Groups.Group { Id = groupId, Name = "Kader", Type = Drammers.Modules.Membership.Groups.GroupType.Committee });
            db.GroupMemberships.Add(new Drammers.Modules.Membership.Groups.GroupMembership { GroupId = groupId, MemberId = inGroep });
            var partner = await db.Members.SingleAsync(m => m.Id == tweede);
            partner.MembershipKind = MembershipKind.Partner;
            partner.PayerMemberId = hoofdlid;
            return await db.SaveChangesAsync();
        });

        var options = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/account-welcomes/options");
        Assert.Equal(2, options.GetProperty("splitMembers").GetInt32());
        Assert.Contains(options.GetProperty("groups").EnumerateArray(), g => g.GetProperty("name").GetString() == "Kader");

        var selection = new { everyone = false, roleCodes = new[] { DefaultRoles.Bestuur }, groupIds = new[] { groupId }, committee = false, split = true, memberIds = new[] { los } };
        var preview = await (await _bestuur.PostAsJsonAsync("/api/v1/admin/account-welcomes/preview", selection)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, preview.GetProperty("members").GetInt32());
        Assert.Equal(1, preview.GetProperty("otherAccounts").GetInt32()); // het bestuur (account zonder lid)
        Assert.Equal(5, preview.GetProperty("recipients").GetArrayLength());

        var send = await _bestuur.PostAsJsonAsync("/api/v1/admin/account-welcomes/send", selection);
        Assert.Equal(HttpStatusCode.Accepted, send.StatusCode);
        await RunOutboxAsync();
        Assert.Equal(
            ["bestuur@example.com", "groep@example.com", "hoofd@example.com", "los@example.com", "tweede@example.com"],
            _api.Emails.Sent.Select(m => m.To).Order().ToArray());

        // Op 11-11 de rest: wie al een welkomstmail kreeg, wordt overgeslagen.
        var everyone = await (await _bestuur.PostAsJsonAsync("/api/v1/admin/account-welcomes/send", new { everyone = true, committee = false, split = false }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, everyone.GetProperty("queued").GetInt32());
        await RunOutboxAsync();
        Assert.Equal("rest@example.com", _api.Emails.Sent.Last().To);

        // Een lege selectie (iedereen heeft al een mail) geeft een melding in plaats van niets.
        Assert.False((await _bestuur.PostAsJsonAsync("/api/v1/admin/account-welcomes/send", selection)).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Bestaand_account_met_e_mail_en_code_wordt_een_wachtwoordaccount_met_behoud_van_alles()
    {
        var memberId = await AddMemberAsync("0601", "code@example.com");
        // Zelf ingelogd met e-mail + code (zoals vóór de omschakeling) en gekoppeld aan het lid, met testtoegang.
        var oldObjectId = _api.Entra.SignUp("code@example.com");
        _api.Entra.TestAccess[oldObjectId] = "dev";
        var userId = await WithDbAsync(async db =>
        {
            var user = new User
            {
                Id = IdGenerator.NewId(),
                ExternalObjectId = oldObjectId,
                Email = "code@example.com",
                DisplayName = "Piet Code",
                MemberId = memberId,
                AccountStatus = AccountStatus.Active,
            };
            var lid = await db.Roles.Where(r => r.Code == DefaultRoles.Lid).Select(r => r.Id).SingleAsync();
            user.Roles.Add(new UserRole { UserId = user.Id, RoleId = lid, AssignedAt = DateTime.UtcNow });
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user.Id;
        });

        Assert.Equal(HttpStatusCode.Accepted, (await _bestuur.PostAsync($"/api/v1/admin/members/{memberId}/welcome", null)).StatusCode);
        await RunOutboxAsync();

        var user = await WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));
        Assert.NotEqual(oldObjectId, user.ExternalObjectId);
        Assert.True(_api.Entra.PasswordSignIn.ContainsKey(user.ExternalObjectId));
        Assert.Contains(oldObjectId, _api.Entra.Deleted);
        Assert.Equal("dev", _api.Entra.TestAccess[user.ExternalObjectId]);
        Assert.NotNull(user.WelcomeSentAt);
        Assert.Contains(_api.Emails.Sent, m => m.To == "code@example.com" && m.PlainText.Contains("Wachtwoord vergeten?", StringComparison.Ordinal));

        // Het nieuwe account is hetzelfde account: zelfde lid en rollen.
        var me = await _api.ClientFor(user.ExternalObjectId).GetFromJsonAsync<JsonElement>("/api/v1/me/member");
        Assert.Equal("0601", me.GetProperty("memberNumber").GetString());
        Assert.Equal(1, await WithDbAsync(db => db.Users.CountAsync(u => u.Email == "code@example.com")));
    }

    [Fact]
    public async Task Welkomstmail_voor_een_kind_kan_niet()
    {
        var kind = await AddMemberAsync("0401", "kind2@example.com", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-8));

        Assert.Equal(HttpStatusCode.Conflict, (await _bestuur.PostAsync($"/api/v1/admin/members/{kind}/welcome", null)).StatusCode);
    }
}
