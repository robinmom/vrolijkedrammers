using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Notifications;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Guardians;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Identifiers;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 10: pushmeldingen met inbox, voorkeuren, doelgroepen, planning en afleverstatus (ADR-009).</summary>
[Collection(SqlServerCollection.Name)]
public class NotificationTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private int _tokens;

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

    private string NewToken() => $"ExponentPushToken[test-{Interlocked.Increment(ref _tokens):D4}-abcdefgh]";

    /// <summary>Gebruiker met rollen, een aangemeld apparaat en (optioneel) een push-token.</summary>
    private async Task<(Guid UserId, HttpClient Client, string? Token)> UserAsync(string email, bool withToken = true, params string[] roles)
    {
        var (userId, oid) = await _api.CreateUserAsync(email, roles);
        var client = _api.ClientFor(oid);
        if (!withToken)
        {
            return (userId, client, null);
        }

        var device = await (await client.PostAsJsonAsync("/api/v1/me/devices", new { installationId = Guid.NewGuid().ToString("N"), platform = "Android", model = "Pixel", appVersion = "1.0.0" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var token = NewToken();
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PutAsJsonAsync($"/api/v1/me/devices/{device.GetProperty("id").GetGuid()}/push-token", new { token })).StatusCode);
        return (userId, client, token);
    }

    /// <summary>Verwerkt de meldingsberichten in de outbox zoals de worker (tijdstippen genegeerd, tenzij <paramref name="dueOnly"/>).</summary>
    private async Task RunOutboxAsync(bool dueOnly = false)
    {
        string[] types = [NotificationAdministration.DispatchMessageType, NotificationAdministration.ReceiptsMessageType];
        while (true)
        {
            var now = _api.Clock.UtcNow.UtcDateTime;
            var messages = await WithDbAsync(db => db.Outbox.AsNoTracking()
                .Where(m => types.Contains(m.Type) && m.ProcessedAt == null && (!dueOnly || m.LockedUntil == null || m.LockedUntil <= now))
                .OrderBy(m => m.CreatedAt).ToListAsync());
            if (messages.Count == 0)
            {
                return;
            }

            foreach (var message in messages)
            {
                using var scope = _api.Services.CreateScope();
                var handler = scope.ServiceProvider.GetServices<IOutboxMessageHandler>().Single(h => h.Type == message.Type);
                await handler.HandleAsync(new OutboxEnvelope(message.Id, message.Type, message.Payload, 0), CancellationToken.None);
                await WithDbAsync(db => db.Outbox.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, DateTime.UtcNow)));
            }
        }
    }

    private static object Audience(bool everyone = false, bool members = false, string[]? roles = null, Guid[]? groups = null, Guid[]? memberIds = null) =>
        new { everyone, members, roles = roles ?? [], groups = groups ?? [], memberIds = memberIds ?? [] };

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string title, object audience, string category = "Program", DateTimeOffset? scheduledAt = null) =>
        client.PostAsJsonAsync("/api/v1/admin/notifications", new { title, body = "Tekst van de melding", category, audience, deepLink = "drammers://agenda", scheduledAt });

    private static async Task<Guid> IdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private IEnumerable<string> PushedTitlesTo(string? token) => _api.Push.Sent.Where(m => m.Token == token).Select(m => m.Title);

    [Fact]
    public async Task Melding_aan_rol_Kaderlid_bereikt_alleen_kaderleden_met_statistiek()
    {
        var redactie = await UserAsync("redactie@example.com", true, DefaultRoles.Redactie);
        var kader = await UserAsync("kader@example.com", true, DefaultRoles.Lid, DefaultRoles.Kaderlid);
        var lid = await UserAsync("lid@example.com", true, DefaultRoles.Lid);

        var preview = await (await redactie.Client.PostAsJsonAsync("/api/v1/admin/notifications/preview-audience",
            new { audience = Audience(roles: [DefaultRoles.Kaderlid]), category = "Kader" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((1, 1), (preview.GetProperty("accounts").GetInt32(), preview.GetProperty("pushDevices").GetInt32()));

        var id = await IdAsync(await SendAsync(redactie.Client, "Kaderavond", Audience(roles: [DefaultRoles.Kaderlid]), "Kader"));
        await RunOutboxAsync();

        Assert.Equal(["Kaderavond"], PushedTitlesTo(kader.Token));
        Assert.Empty(PushedTitlesTo(lid.Token));
        var message = _api.Push.Sent.Single();
        Assert.Equal(("kader", id.ToString(), "drammers://agenda"), (message.ChannelId, message.Data["notificationId"], message.Data["url"]));
        Assert.Equal(1, message.Badge);

        var tweede = await IdAsync(await SendAsync(redactie.Client, "Kaderavond verplaatst", Audience(roles: [DefaultRoles.Kaderlid]), "Kader"));
        await RunOutboxAsync();
        Assert.Equal(2, _api.Push.Sent.Single(m => m.Data["notificationId"] == tweede.ToString()).Badge);
        Assert.Equal(HttpStatusCode.NoContent, (await kader.Client.PostAsync($"/api/v1/me/notifications/{tweede}/read", null)).StatusCode);

        var inbox = await kader.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications");
        Assert.Equal(1, inbox.GetProperty("unreadCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await kader.Client.PostAsync($"/api/v1/me/notifications/{id}/read", null)).StatusCode);
        Assert.Empty((await lid.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("items").EnumerateArray());

        var detail = await redactie.Client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/notifications/{id}");
        var summary = detail.GetProperty("summary");
        Assert.Equal(("Sent", 1, 1, 1, 1), (summary.GetProperty("status").GetString(), summary.GetProperty("recipientCount").GetInt32(),
            summary.GetProperty("pushCount").GetInt32(), summary.GetProperty("deliveredCount").GetInt32(), summary.GetProperty("readCount").GetInt32()));
        Assert.Contains("Rol: Kaderlid", detail.GetProperty("audienceLabels").EnumerateArray().Select(l => l.GetString()));
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "notification.sent" && a.EntityId == id.ToString())));
    }

    [Fact]
    public async Task Wie_Nieuws_uitzet_krijgt_geen_nieuwspush_maar_wel_Dringend_en_ziet_beide_in_de_inbox()
    {
        var lid = await UserAsync("lid@example.com", true, DefaultRoles.Lid);
        var set = await lid.Client.PutAsJsonAsync("/api/v1/me/notification-preferences", new { preferences = new[] { new { category = "News", enabled = false } } });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var urgentOff = await lid.Client.PutAsJsonAsync("/api/v1/me/notification-preferences", new { preferences = new[] { new { category = "Urgent", enabled = false } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, urgentOff.StatusCode);

        var news = await IdAsync(await SendAsync(_bestuur, "Nieuwsbrief", Audience(members: true), "News"));
        await IdAsync(await SendAsync(_bestuur, "Optocht gaat niet door", Audience(members: true), "Urgent"));
        await RunOutboxAsync();

        Assert.Equal(["Optocht gaat niet door"], PushedTitlesTo(lid.Token));
        var inbox = await lid.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications");
        Assert.Equal(2, inbox.GetProperty("items").GetArrayLength());
        Assert.Equal(1, (await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/notifications/{news}")).GetProperty("optedOut").GetInt32());
    }

    [Fact]
    public async Task Gast_met_push_krijgt_meldingen_voor_iedereen_maar_niet_voor_leden()
    {
        var guest = _api.CreateClient();
        var token = NewToken();
        Assert.Equal(HttpStatusCode.NoContent,
            (await guest.PostAsJsonAsync("/api/v1/push-devices/anonymous", new { installId = "install-guest-1", platform = "Ios", token })).StatusCode);

        await IdAsync(await SendAsync(_bestuur, "Voor iedereen", Audience(everyone: true)));
        await IdAsync(await SendAsync(_bestuur, "Alleen leden", Audience(members: true)));
        await RunOutboxAsync();

        Assert.Equal(["Voor iedereen"], PushedTitlesTo(token));
        var stored = await WithDbAsync(db => db.PushDevices.AsNoTracking().SingleAsync(p => p.AnonymousInstallId == "install-guest-1"));
        Assert.NotEqual(token, stored.ProtectedToken);
        Assert.Equal(PushTokenProtector.Hash(token), stored.TokenHash);
    }

    [Fact]
    public async Task Geplande_melding_gaat_op_tijd_uit_en_een_geannuleerde_niet()
    {
        var lid = await UserAsync("lid@example.com", true, DefaultRoles.Lid);
        var later = _api.Clock.UtcNow.AddHours(2);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await SendAsync(_bestuur, "Te vroeg", Audience(members: true), scheduledAt: _api.Clock.UtcNow.AddMinutes(-5))).StatusCode);
        var planned = await IdAsync(await SendAsync(_bestuur, "Gepland", Audience(members: true), scheduledAt: later));
        var canceled = await IdAsync(await SendAsync(_bestuur, "Geannuleerd", Audience(members: true), scheduledAt: later));
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/notifications/{canceled}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _bestuur.PostAsync($"/api/v1/admin/notifications/{canceled}/cancel", null)).StatusCode);

        await RunOutboxAsync(dueOnly: true);
        Assert.Empty(_api.Push.Sent);
        Assert.Empty((await lid.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("items").EnumerateArray());

        _api.Clock.Advance(TimeSpan.FromHours(3));
        await RunOutboxAsync(dueOnly: true);
        Assert.Equal(["Gepland"], PushedTitlesTo(lid.Token));
        var statuses = await WithDbAsync(db => db.Notifications.AsNoTracking().ToDictionaryAsync(n => n.Id, n => n.Status));
        Assert.Equal((NotificationStatus.Sent, NotificationStatus.Canceled), (statuses[planned], statuses[canceled]));
    }

    [Fact]
    public async Task Rechten_Dringend_en_iedereen_alleen_met_urgent_en_groepsrecht_alleen_voor_de_eigen_groep()
    {
        var redactie = await UserAsync("redactie@example.com", false, DefaultRoles.Redactie);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(redactie.Client, "Iedereen", Audience(everyone: true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(redactie.Client, "Dringend", Audience(members: true), "Urgent")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(redactie.Client, "Leden", Audience(members: true))).StatusCode);

        // Dansgarde-leiding heeft alleen notification.send.group.
        var own = await CreateGroupAsync("Dansgarde");
        var other = await CreateGroupAsync("Jeugdcommissie");
        var (leaderMember, leaderClient) = await MemberWithAccountAsync("300", DefaultRoles.DansgardeLeiding);
        await AddToGroupAsync(own, leaderMember);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(leaderClient, "Eigen groep", Audience(groups: [own]), "DanceGuard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(leaderClient, "Andere groep", Audience(groups: [other]), "DanceGuard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(leaderClient, "Rol", Audience(roles: [DefaultRoles.Lid]), "DanceGuard")).StatusCode);
        var visible = await leaderClient.GetFromJsonAsync<JsonElement>("/api/v1/admin/notifications");
        Assert.Equal(["Eigen groep"], visible.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()));

        var lid = await UserAsync("lid@example.com", false, DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(lid.Client, "Mag niet", Audience(members: true))).StatusCode);
    }

    [Fact]
    public async Task Ouder_ontvangt_namens_het_kind_in_de_groep_en_een_verlopen_token_wordt_uitgezet()
    {
        var group = await CreateGroupAsync("Dansgarde");
        var child = await WithDbAsync(async db =>
        {
            var member = new Member { Id = IdGenerator.NewId(), MemberNumber = "401", FullName = "Sanne Lid", FirstName = "Sanne", MembershipStatus = MembershipStatus.Active };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            return member.Id;
        });
        await AddToGroupAsync(group, child);
        var parent = await UserAsync("ouder@example.com", true, DefaultRoles.Ouder);
        await WithDbAsync(async db =>
        {
            db.GuardianRelations.Add(new GuardianRelation { Id = IdGenerator.NewId(), MemberId = child, GuardianUserId = parent.UserId, GuardianName = "Ouder", VerifiedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        });
        var stale = await UserAsync("oud-toestel@example.com", true, DefaultRoles.Lid);
        _api.Push.Unregistered[stale.Token!] = true;

        var id = await IdAsync(await SendAsync(_bestuur, "Training vervalt", Audience(groups: [group], roles: [DefaultRoles.Lid]), "DanceGuard"));
        await RunOutboxAsync();

        Assert.Equal(["Namens Sanne: Training vervalt"], PushedTitlesTo(parent.Token));
        Assert.Equal("Namens Sanne: Training vervalt", (await parent.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("items")[0].GetProperty("title").GetString());
        Assert.False(await WithDbAsync(db => db.PushDevices.Where(p => p.UserId == stale.UserId).Select(p => p.Enabled).SingleAsync()));
        var summary = (await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/notifications/{id}")).GetProperty("summary");
        Assert.Equal(("PartiallyFailed", 1), (summary.GetProperty("status").GetString(), summary.GetProperty("failedCount").GetInt32()));
    }

    [Fact]
    public async Task Nieuws_met_push_bij_publicatie_geeft_een_melding_aan_de_doelgroep_en_maar_een_keer()
    {
        var lid = await UserAsync("lid@example.com", true, DefaultRoles.Lid);
        var redactie = await UserAsync("redactie@example.com", false, DefaultRoles.Redactie);
        object News(string visibility, string status) => new
        {
            title = "Nieuwe prins bekend",
            summary = "Wie wordt de nieuwe prins?",
            body = "**Groot nieuws** voor Loil.",
            category = (string?)null,
            expireAt = (DateTimeOffset?)null,
            publication = new { visibility, audienceRoles = Array.Empty<string>(), status, publishAt = (DateTimeOffset?)null },
            pushOnPublish = true,
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.Client.PostAsJsonAsync("/api/v1/admin/news", News("Public", "Draft"))).StatusCode);
        var created = await redactie.Client.PostAsJsonAsync("/api/v1/admin/news", News("Members", "Draft"));
        var newsId = await IdAsync(created);
        Assert.False(await WithDbAsync(db => db.Notifications.AnyAsync()));

        Assert.Equal(HttpStatusCode.NoContent, (await redactie.Client.PostAsync($"/api/v1/admin/news/{newsId}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await redactie.Client.PutAsJsonAsync($"/api/v1/admin/news/{newsId}", News("Members", "Published"))).StatusCode);
        await RunOutboxAsync();

        Assert.Equal(["Nieuwe prins bekend"], PushedTitlesTo(lid.Token));
        var notification = await WithDbAsync(db => db.Notifications.AsNoTracking().SingleAsync());
        Assert.Equal((NewsPush.SourceType, newsId, $"drammers://nieuws/{newsId}", "Wie wordt de nieuwe prins?", NotificationCategory.News),
            (notification.SourceType, notification.SourceId, notification.DeepLink, notification.Body, notification.Category));
        Assert.Equal("Sent", (await redactie.Client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/news/{newsId}")).GetProperty("pushStatus").GetString());
    }

    [Fact]
    public async Task Zonder_token_toch_in_de_inbox_en_afmelden_van_het_apparaat_verwijdert_het_token()
    {
        var zonder = await UserAsync("zonder@example.com", false, DefaultRoles.Lid);
        var met = await UserAsync("met@example.com", true, DefaultRoles.Lid);
        var id = await IdAsync(await SendAsync(_bestuur, "Iedereen welkom", Audience(members: true)));
        await RunOutboxAsync();

        Assert.Equal(1, (await zonder.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/notifications")).GetProperty("unreadCount").GetInt32());
        Assert.Equal(1, (await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/notifications/{id}")).GetProperty("noDevice").GetInt32());

        var device = (await met.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/devices"))[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await met.Client.DeleteAsync($"/api/v1/me/devices/{device}")).StatusCode);
        Assert.False(await WithDbAsync(db => db.PushDevices.AnyAsync(p => p.UserId == met.UserId)));
    }

    [Fact]
    public async Task Push_tokens_die_een_half_jaar_niet_vernieuwd_zijn_worden_opgeruimd()
    {
        var oud = await UserAsync("oud@example.com", true, DefaultRoles.Lid);
        var nieuw = await UserAsync("nieuw@example.com", true, DefaultRoles.Lid);
        await WithDbAsync(db => db.PushDevices.Where(p => p.UserId == oud.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastRegisteredAt, _api.Clock.UtcNow.UtcDateTime.AddDays(-200))));

        using var scope = _api.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<Drammers.Infrastructure.Configuration.DataRetentionJob>().RunAsync(default);

        Assert.Equal(1, result.PushDevices);
        Assert.False(await WithDbAsync(db => db.PushDevices.AnyAsync(p => p.UserId == oud.UserId)));
        Assert.True(await WithDbAsync(db => db.PushDevices.AnyAsync(p => p.UserId == nieuw.UserId)));
    }

    private async Task<Guid> CreateGroupAsync(string name)
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/groups", new { name, description = (string?)null, type = "Committee", carnivalYearId = (int?)null });
        return await IdAsync(response);
    }

    private async Task AddToGroupAsync(Guid group, Guid member) =>
        Assert.Equal(HttpStatusCode.NoContent,
            (await _bestuur.PutAsJsonAsync($"/api/v1/admin/groups/{group}/members/{member}", new { function = "Member", validFrom = (string?)null, validTo = (string?)null })).StatusCode);

    /// <summary>Lid met een account met de gegeven rol (plus Lid); geeft het lid-id en de client.</summary>
    private async Task<(Guid MemberId, HttpClient Client)> MemberWithAccountAsync(string number, string role)
    {
        var (userId, oid) = await _api.CreateUserAsync($"lid{number}@example.com", DefaultRoles.Lid, role);
        var memberId = await WithDbAsync(async db =>
        {
            var member = new Member { Id = IdGenerator.NewId(), MemberNumber = number, FullName = $"Lid {number}", MembershipStatus = MembershipStatus.Active };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = member.Id;
            await db.SaveChangesAsync();
            return member.Id;
        });
        return (memberId, _api.ClientFor(oid));
    }
}
