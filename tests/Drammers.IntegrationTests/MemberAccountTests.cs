using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Api.Controllers;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Identity.AccountRequests;
using Drammers.Modules.Identity.Provisioning;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 9a: accountverzoeken en provisioning (ADR-014), eigen gegevens, apparaten, account verwijderen, AVG.</summary>
[Collection(SqlServerCollection.Name)]
public class MemberAccountTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private HttpClient _anonymous = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        _anonymous = _api.CreateClient();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private Task<Guid> AddMemberAsync(string number, string? email, MembershipStatus status = MembershipStatus.Active) =>
        WithDbAsync(async db =>
        {
            var member = new Member
            {
                Id = IdGenerator.NewId(),
                MemberNumber = number,
                FullName = $"Piet Lid{number}",
                FirstName = "Piet",
                Email = email,
                City = "Loil",
                MembershipStatus = status,
            };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            return member.Id;
        });

    private Task<HttpResponseMessage> RequestAccountAsync(string number, string email) =>
        _anonymous.PostAsJsonAsync("/api/v1/account-requests", new { memberNumber = number, email });

    /// <summary>Verwerkt de openstaande provisioning-berichten zoals de worker dat doet; geeft het aantal fouten terug.</summary>
    private async Task<int> RunProvisioningAsync()
    {
        var messages = await WithDbAsync(db => db.Outbox.AsNoTracking()
            .Where(m => m.Type == MemberAccounts.ProvisionMessageType).OrderBy(m => m.CreatedAt).ToListAsync());
        await WithDbAsync(db => db.Outbox.Where(m => m.Type == MemberAccounts.ProvisionMessageType).ExecuteDeleteAsync());
        var failures = 0;
        foreach (var message in messages)
        {
            using var scope = _api.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<MemberAccounts>().RunProvisioningAsync(
                    JsonSerializer.Deserialize<MemberAccounts.ProvisionMessage>(message.Payload, JsonSerializerOptions.Web)!, CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                failures++;
            }
        }

        return failures;
    }

    private HttpClient ClientForMemberAsync(string email)
    {
        Assert.True(_api.Entra.AccountsByEmail.TryGetValue(email, out var objectId));
        return _api.ClientFor(objectId!);
    }

    [Fact]
    public async Task Exacte_match_geeft_account_welkomstmail_en_eigen_gegevens()
    {
        await AddMemberAsync("0101", "Piet@Example.com");

        var response = await RequestAccountAsync("0101", "piet@example.com");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(AccountRequestsController.GenericMessage, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        Assert.Equal(0, await RunProvisioningAsync());
        Assert.Equal(1, _api.Entra.CreateCalls);
        var mail = Assert.Single(_api.Emails.Sent);
        Assert.Equal("piet@example.com", mail.To);
        Assert.Contains("Beste Piet", mail.PlainText, StringComparison.Ordinal);

        var member = ClientForMemberAsync("piet@example.com");
        var me = await member.GetFromJsonAsync<JsonElement>("/api/v1/me/member");
        Assert.Equal("0101", me.GetProperty("memberNumber").GetString());
        Assert.Equal("Loil", me.GetProperty("city").GetString());
        var roles = (await member.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("code").GetString());
        Assert.Contains(DefaultRoles.Lid, roles);
    }

    [Fact]
    public async Task Mismatch_geeft_hetzelfde_antwoord_geen_account_en_komt_in_de_wachtrij()
    {
        await AddMemberAsync("0201", "echt@example.com");

        var match = await RequestAccountAsync("0201", "echt@example.com");
        var wrongEmail = await RequestAccountAsync("0201", "ander@example.com");
        var unknownNumber = await RequestAccountAsync("9999", "wie@example.com");

        string[] bodies = [await match.Content.ReadAsStringAsync(), await wrongEmail.Content.ReadAsStringAsync(), await unknownNumber.Content.ReadAsStringAsync()];
        Assert.All([match, wrongEmail, unknownNumber], r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        Assert.Single(bodies.Distinct());

        await RunProvisioningAsync();
        Assert.Equal(1, _api.Entra.CreateCalls);
        Assert.False(_api.Entra.AccountsByEmail.ContainsKey("ander@example.com"));

        var queue = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/account-requests?status=Pending");
        var items = queue.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var mismatch = items.Single(i => i.GetProperty("email").GetString() == "ander@example.com");
        Assert.Equal("email-mismatch", mismatch.GetProperty("mismatchReason").GetString());
        Assert.Equal("0201", mismatch.GetProperty("member").GetProperty("memberNumber").GetString());
        Assert.Equal(JsonValueKind.Null, items.Single(i => i.GetProperty("memberNumber").GetString() == "9999").GetProperty("member").ValueKind);
    }

    [Fact]
    public async Task Bestuur_keurt_een_verzoek_goed_na_correctie_en_het_account_krijgt_het_adres_uit_e_Boekhouden()
    {
        var memberId = await AddMemberAsync("0301", "oud@example.com");
        await RequestAccountAsync("0301", "nieuw@example.com");
        var requestId = await WithDbAsync(db => db.AccountRequests.Where(r => r.MemberNumber == "0301").Select(r => r.Id).SingleAsync());

        // Het secretariaat corrigeert het adres in e-Boekhouden; de sync neemt het over.
        await WithDbAsync(async db =>
        {
            (await db.Members.SingleAsync(m => m.Id == memberId)).Email = "nieuw@example.com";
            return await db.SaveChangesAsync();
        });
        var approve = await _bestuur.PostAsJsonAsync($"/api/v1/admin/account-requests/{requestId}/approve", new { memberId });
        Assert.Equal(HttpStatusCode.NoContent, approve.StatusCode);
        await RunProvisioningAsync();

        Assert.True(_api.Entra.AccountsByEmail.ContainsKey("nieuw@example.com"));
        var again = await _bestuur.PostAsJsonAsync($"/api/v1/admin/account-requests/{requestId}/approve", new { memberId });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Dubbel_verzoek_maakt_geen_tweede_account()
    {
        await AddMemberAsync("0401", "dubbel@example.com");
        await RequestAccountAsync("0401", "dubbel@example.com");
        await RunProvisioningAsync();

        // Opnieuw (na het duplicaatvenster, dus een nieuw verzoek): het lid heeft al een account.
        _api.Clock.Advance(TimeSpan.FromDays(2));
        await RequestAccountAsync("0401", "dubbel@example.com");
        await RunProvisioningAsync();

        Assert.Equal(1, _api.Entra.CreateCalls);
        Assert.Equal(1, await WithDbAsync(db => db.Users.CountAsync(u => u.Email == "dubbel@example.com")));
        Assert.Equal(AccountRequestStatus.Duplicate, await WithDbAsync(db => db.AccountRequests.OrderByDescending(r => r.RequestedAt).Select(r => r.Status).FirstAsync()));
    }

    [Fact]
    public async Task Provisioning_die_halverwege_faalt_is_zichtbaar_en_slaagt_na_opnieuw_proberen_zonder_dubbelen()
    {
        var memberId = await AddMemberAsync("0501", "retry@example.com");
        _api.Entra.FailNextCreate = true;

        var start = await _bestuur.PostAsync($"/api/v1/admin/members/{memberId}/provision-account", null);
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal(1, await RunProvisioningAsync());

        var open = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/account-provisioning")).EnumerateArray().Single();
        Assert.Contains("Graph", open.GetProperty("lastError").GetString(), StringComparison.Ordinal);
        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/members/{memberId}");
        Assert.Equal("Pending", detail.GetProperty("provisioning").GetProperty("step").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/account-provisioning/{open.GetProperty("id").GetGuid()}/retry", null)).StatusCode);
        Assert.Equal(0, await RunProvisioningAsync());
        // Nog een keer (bijv. dubbel bericht): niets nieuws.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/account-provisioning/{open.GetProperty("id").GetGuid()}/retry", null)).StatusCode);
        await RunProvisioningAsync();

        Assert.Equal(1, _api.Entra.CreateCalls);
        Assert.Single(_api.Emails.Sent);
        Assert.Equal(ProvisioningStep.Completed, await WithDbAsync(db => db.AccountProvisioning.Where(p => p.MemberId == memberId).Select(p => p.Step).SingleAsync()));
        Assert.Empty((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/account-provisioning")).EnumerateArray());
        var conflict = await _bestuur.PostAsync($"/api/v1/admin/members/{memberId}/provision-account", null);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Apparaat_aanmelden_hernoemen_en_afmelden_waarna_dat_apparaat_is_uitgelogd()
    {
        var (_, oid) = await _api.CreateUserAsync("apparaat@example.com", DefaultRoles.Lid);
        var phone = _api.ClientFor(oid);
        phone.DefaultRequestHeaders.Add("X-Device-Id", "installatie-telefoon-0001");
        var tablet = _api.ClientFor(oid);
        tablet.DefaultRequestHeaders.Add("X-Device-Id", "installatie-tablet-00002");

        var register = await phone.PostAsJsonAsync("/api/v1/me/devices", new { installationId = "installatie-telefoon-0001", platform = "Ios", model = "iPhone 15", appVersion = "1.0.0" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        await tablet.PostAsJsonAsync("/api/v1/me/devices", new { installationId = "installatie-tablet-00002", platform = "Android", model = "Pixel 8", appVersion = "1.0.0" });

        var devices = (await phone.GetFromJsonAsync<JsonElement>("/api/v1/me/devices")).EnumerateArray().ToList();
        Assert.Equal(2, devices.Count);
        Assert.True(devices.Single(d => d.GetProperty("name").GetString() == "iPhone 15").GetProperty("current").GetBoolean());
        var tabletId = devices.Single(d => d.GetProperty("name").GetString() == "Pixel 8").GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await phone.PatchAsJsonAsync($"/api/v1/me/devices/{tabletId}", new { name = "Tablet thuis" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await phone.DeleteAsync($"/api/v1/me/devices/{tabletId}")).StatusCode);

        var denied = await tablet.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("DEVICE_REVOKED", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/v1/me")).StatusCode);
        var reRegister = await _api.ClientFor(oid).PostAsJsonAsync("/api/v1/me/devices", new { installationId = "installatie-tablet-00002", platform = "Android", model = "Pixel 8", appVersion = "1.0.0" });
        Assert.Equal(HttpStatusCode.Conflict, reRegister.StatusCode);
        Assert.Single((await phone.GetFromJsonAsync<JsonElement>("/api/v1/me/devices")).EnumerateArray());
    }

    [Fact]
    public async Task Beheerder_ziet_apparaten_en_kan_er_een_intrekken()
    {
        var (userId, oid) = await _api.CreateUserAsync("lid-apparaat@example.com", DefaultRoles.Lid);
        var phone = _api.ClientFor(oid);
        phone.DefaultRequestHeaders.Add("X-Device-Id", "installatie-beheer-00001");
        await phone.PostAsJsonAsync("/api/v1/me/devices", new { installationId = "installatie-beheer-00001", platform = "Ios", model = (string?)null, appVersion = "1.0.0" });

        var devices = (await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/users/{userId}/devices")).EnumerateArray().ToList();
        Assert.Equal("iPhone", Assert.Single(devices).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/devices/{devices[0].GetProperty("id").GetGuid()}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/v1/me")).StatusCode);

        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync($"/api/v1/admin/users/{userId}/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/account-requests")).StatusCode);
    }

    [Fact]
    public async Task Account_verwijderen_verwijdert_ook_het_Entra_account_maar_niet_het_lid()
    {
        var memberId = await AddMemberAsync("0601", "weg@example.com");
        await RequestAccountAsync("0601", "weg@example.com");
        await RunProvisioningAsync();
        var member = ClientForMemberAsync("weg@example.com");
        var objectId = _api.Entra.AccountsByEmail["weg@example.com"];

        var wrong = await member.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new { confirmation = "ja" }) });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
        var delete = await member.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me") { Content = JsonContent.Create(new { confirmation = "VERWIJDEREN" }) });
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Contains(objectId, _api.Entra.Deleted);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/me")).StatusCode);
        var user = await WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.ExternalObjectId == objectId));
        Assert.Equal((AccountStatus.Deleted, (Guid?)null), (user.AccountStatus, user.MemberId));
        Assert.DoesNotContain("weg@", user.Email, StringComparison.Ordinal);
        Assert.True(await WithDbAsync(db => db.Members.AnyAsync(m => m.Id == memberId)));

        // Het lid kan later opnieuw een account aanvragen.
        _api.Clock.Advance(TimeSpan.FromDays(2));
        await RequestAccountAsync("0601", "weg@example.com");
        Assert.Equal(0, await RunProvisioningAsync());
        Assert.Equal(2, _api.Entra.CreateCalls);
    }

    [Fact]
    public async Task Privacy_export_bevat_alle_gegevenscategorieen_en_is_24_uur_te_downloaden()
    {
        await AddMemberAsync("0701", "avg@example.com");
        await RequestAccountAsync("0701", "avg@example.com");
        await RunProvisioningAsync();
        var member = ClientForMemberAsync("avg@example.com");
        member.DefaultRequestHeaders.Add("X-Device-Id", "installatie-avg-000000001");
        await member.PostAsJsonAsync("/api/v1/me/devices", new { installationId = "installatie-avg-000000001", platform = "Android", model = "Pixel", appVersion = "1.0.0" });

        var export = await (await member.PostAsync("/api/v1/me/privacy/export", null)).Content.ReadFromJsonAsync<JsonElement>();
        var json = await new HttpClient().GetFromJsonAsync<JsonElement>(export.GetProperty("downloadUrl").GetString());
        foreach (var category in new[] { "account", "member", "groups", "roles", "devices", "logins", "accountRequests", "privacyRequests" })
        {
            Assert.True(json.TryGetProperty(category, out _), $"Categorie {category} ontbreekt");
        }

        Assert.Equal("0701", json.GetProperty("member").GetProperty("memberNumber").GetString());
        Assert.Equal("Pixel", json.GetProperty("devices")[0].GetProperty("model").GetString());

        var id = export.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/me/privacy/export/{id}")).StatusCode);
        _api.Clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/me/privacy/export/{id}")).StatusCode);
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "privacy.exported")));
    }

    [Fact]
    public async Task Accountverzoeken_zijn_beperkt_per_IP()
    {
        await using var strict = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync()) { AnonymousFormsLimit = 2 };
        var client = strict.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/v1/account-requests", new { memberNumber = $"{i}", email = $"x{i}@example.com" })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task App_aanmeldconfiguratie_en_doorstuurpagina()
    {
        var config = await _anonymous.GetFromJsonAsync<JsonElement>("/api/v1/app-auth-config");
        Assert.Contains("offline_access", config.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(JsonValueKind.Null, config.GetProperty("redirectBridgeUrl").ValueKind);

        var state = "abc." + Convert.ToBase64String("exp://192.168.1.10:8081/--/auth"u8.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal("exp://192.168.1.10:8081/--/auth", AppAuthController.ReturnUrl(state));
        var web = "abc." + Convert.ToBase64String("https://evil.example.com"u8.ToArray()).TrimEnd('=');
        Assert.Null(AppAuthController.ReturnUrl(web));
        Assert.Null(AppAuthController.ReturnUrl("zonder-punt"));
    }
}
