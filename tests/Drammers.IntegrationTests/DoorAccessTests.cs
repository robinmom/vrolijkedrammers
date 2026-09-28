using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.Shared;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Qr;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 14a: toegangscontrole bij de deur — scannen (groen/oranje/rood), beslissen, inchecken en de scanlog.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class DoorAccessTests(SqlServerFixture sql) : IAsyncLifetime, IDisposable
{
    /// <summary>Zondag 7 februari 2027, 20:00 UTC: tijdens carnaval (6–9 februari).</summary>
    private static readonly DateTimeOffset Evening = new(2027, 2, 7, 20, 0, 0, TimeSpan.Zero);

    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private readonly List<ECDsa> _keys = [];

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    public void Dispose() => _keys.ForEach(k => k.Dispose());

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return response.StatusCode == HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private void ToEvening() => _api.Clock.Advance(Evening - _api.Clock.UtcNow);

    private Task<Guid> EventAsync(bool accessControl = true) => WithDbAsync(async db =>
    {
        var id = IdGenerator.NewId();
        db.Events.Add(new Event
        {
            Id = id,
            CarnivalYearId = 1,
            CategoryId = 1,
            Title = "Carnavalsavond",
            StartAt = Evening.UtcDateTime.AddHours(-1),
            EndAt = Evening.UtcDateTime.AddHours(5),
            Visibility = ContentVisibility.Public,
            Status = PublicationStatus.Published,
            AccessControl = accessControl,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    });

    private async Task<(Guid MemberId, string ObjectId)> MemberAsync(string email, MembershipStatus status = MembershipStatus.Active)
    {
        var (userId, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid);
        var memberId = IdGenerator.NewId();
        await WithDbAsync(async db =>
        {
            db.Members.Add(new Member { Id = memberId, MemberNumber = email[..4], FullName = $"Lid {email[..4]}", MembershipStatus = status });
            await db.SaveChangesAsync();
            (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = memberId;
            return await db.SaveChangesAsync();
        });
        return (memberId, oid);
    }

    private async Task<HttpClient> DeviceAsync(string objectId, string installationId)
    {
        var client = _api.ClientFor(objectId);
        client.DefaultRequestHeaders.Add("X-Device-Id", installationId);
        await JsonAsync(await client.PostAsJsonAsync("/api/v1/me/devices", new { installationId, platform = "Android", model = "Pixel 8", appVersion = "1.0.0" }));
        return client;
    }

    /// <summary>Lid met gekoppelde hardwaresleutel; geeft een functie die (zoals de app) een verse code maakt.</summary>
    private async Task<(Guid MemberId, Func<Task<string>> Code)> MemberWithQrAsync(string email)
    {
        var (memberId, oid) = await MemberAsync(email);
        var phone = await DeviceAsync(oid, $"installatie-telefoon-{email[..4]}-0001");
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _keys.Add(key);
        await JsonAsync(await phone.PutAsJsonAsync("/api/v1/me/devices/current/key",
            new { publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), securityLevel = "StrongBox" }), HttpStatusCode.NoContent);
        var challenge = (await JsonAsync(await phone.PostAsync("/api/v1/me/ticket/challenge", null))).GetProperty("challenge").GetString()!;
        var signature = key.SignData(Convert.FromBase64String(challenge), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        await JsonAsync(await phone.PostAsJsonAsync("/api/v1/me/ticket/bind-device", new { challenge, signature = Convert.ToBase64String(signature) }), HttpStatusCode.NoContent);
        // Zoals de app: de ticketgegevens één keer ophalen en op het toestel bewaren (ook als het ticket later geblokkeerd is).
        var ticket = await phone.GetFromJsonAsync<JsonElement>("/api/v1/me/ticket");
        return (memberId, () =>
        {
            var unsigned = QrPayload.Unsigned(QrPayload.DeviceSigned, Convert.FromBase64String(ticket.GetProperty("publicRef").GetString()!),
                ticket.GetProperty("credentialVersion").GetInt32(), Convert.FromBase64String(ticket.GetProperty("deviceShortId").GetString()!),
                _api.Clock.UtcNow.ToUnixTimeSeconds(), QrPayload.DefaultValidFor);
            return Task.FromResult(Base45.Encode([.. unsigned, .. key.SignData(unsigned, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)]));
        }
        );
    }

    private async Task<HttpClient> DoorAsync(string email, string installationId)
    {
        var (_, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid, DefaultRoles.Scanner);
        return await DeviceAsync(oid, installationId);
    }

    private static async Task<JsonElement> ScanAsync(HttpClient door, string code) =>
        await JsonAsync(await door.PostAsJsonAsync("/api/v1/access/scan", new { code }));

    [Fact]
    public async Task Groen_bij_de_eerste_keer_en_op_hetzelfde_toestel_oranje_op_een_ander_toestel_rood_met_reden()
    {
        await EventAsync();
        ToEvening();
        var (_, code) = await MemberWithQrAsync("piet@example.com");
        var deur1 = await DoorAsync("deur1@example.com", "installatie-deur-een-0001");
        var deur2 = await DoorAsync("deur2@example.com", "installatie-deur-twee-0002");

        var status = await deur1.GetFromJsonAsync<JsonElement>("/api/v1/access/status");
        Assert.Equal("Carnavalsavond", status.GetProperty("current").GetProperty("title").GetString());

        var first = await ScanAsync(deur1, await code());
        Assert.Equal(("Admitted", "Toegang geldig", "Eerste keer vanavond", "Lid piet"),
            (first.GetProperty("outcome").GetString(), first.GetProperty("title").GetString(), first.GetProperty("message").GetString(), first.GetProperty("holderName").GetString()));
        var again = await ScanAsync(deur1, await code());
        Assert.Equal("AdmittedAgain", again.GetProperty("outcome").GetString());
        Assert.StartsWith("Al eerder gescand op dit toestel om ", again.GetProperty("message").GetString(), StringComparison.Ordinal);

        var other = await ScanAsync(deur2, await code());
        Assert.Equal(("Warning", true), (other.GetProperty("outcome").GetString(), other.GetProperty("needsDecision").GetBoolean()));
        Assert.StartsWith("Vanavond al gescand op een ander toestel om ", other.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("deur1", other.GetProperty("message").GetString(), StringComparison.Ordinal);
        var counts = await JsonAsync(await deur2.PostAsJsonAsync($"/api/v1/access/scans/{other.GetProperty("scanId").GetGuid()}/decision", new { admit = true }));
        Assert.Equal((1, 3, 0), (counts.GetProperty("inside").GetInt32(), counts.GetProperty("scans").GetInt32(), counts.GetProperty("refused").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict, (await deur2.PostAsJsonAsync($"/api/v1/access/scans/{other.GetProperty("scanId").GetGuid()}/decision", new { admit = false })).StatusCode);

        var garbage = await ScanAsync(deur1, "GEEN CODE");
        Assert.Equal(("Refused", "Geen toegang", "Geen geldige QR-code van De Vrolijke Drammers."),
            (garbage.GetProperty("outcome").GetString(), garbage.GetProperty("title").GetString(), garbage.GetProperty("message").GetString()));
        var ticketId = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tickets")).GetProperty("items")[0].GetProperty("id").GetGuid();
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/tickets/{ticketId}/action", new { action = "Block", reason = "Test" }), HttpStatusCode.NoContent);
        var blocked = await ScanAsync(deur1, await code());
        Assert.Equal(("Refused", "Ticket geblokkeerd."), (blocked.GetProperty("outcome").GetString(), blocked.GetProperty("message").GetString()));
        Assert.Equal(2, blocked.GetProperty("counts").GetProperty("refused").GetInt32());

        var events = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/access-scans/events");
        var eventId = events[0].GetProperty("id").GetGuid();
        var log = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/access-scans?eventId={eventId}");
        Assert.Equal(5, log.GetProperty("totalCount").GetInt32());
        Assert.Contains(log.GetProperty("items").EnumerateArray(), r => r.GetProperty("reason").GetString() == "Blocked");
    }

    [Fact]
    public async Task Inchecken_in_het_portal_al_binnen_toch_opnieuw_en_daarna_oranje_bij_de_QR()
    {
        await EventAsync();
        ToEvening();
        var (memberId, code) = await MemberWithQrAsync("marie@example.com");
        var (_, deurOid) = await _api.CreateUserAsync("portaldeur@example.com", DefaultRoles.Lid, DefaultRoles.Scanner);
        var portal = _api.ClientFor(deurOid);

        var card = await portal.GetFromJsonAsync<JsonElement>($"/api/v1/admin/members/{memberId}/access");
        Assert.Equal(("Carnavalsavond", false), (card.GetProperty("current").GetProperty("title").GetString(), card.GetProperty("inside").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, (await portal.GetAsync($"/api/v1/admin/members/{memberId}")).StatusCode);

        var checkedIn = await JsonAsync(await portal.PostAsJsonAsync($"/api/v1/admin/members/{memberId}/check-in", new { force = false }));
        Assert.Equal(("Admitted", "Ingecheckt"), (checkedIn.GetProperty("outcome").GetString(), checkedIn.GetProperty("title").GetString()));
        var already = await JsonAsync(await portal.PostAsJsonAsync($"/api/v1/admin/members/{memberId}/check-in", new { force = false }));
        Assert.Equal(("Warning", "Al binnen", JsonValueKind.Null), (already.GetProperty("outcome").GetString(), already.GetProperty("title").GetString(), already.GetProperty("scanId").ValueKind));
        var forced = await JsonAsync(await portal.PostAsJsonAsync($"/api/v1/admin/members/{memberId}/check-in", new { force = true }));
        Assert.Equal("AdmittedAgain", forced.GetProperty("outcome").GetString());

        var deur = await DoorAsync("deur@example.com", "installatie-deur-een-0001");
        var scan = await ScanAsync(deur, await code());
        Assert.Equal("Warning", scan.GetProperty("outcome").GetString());
        Assert.StartsWith("Vanavond al ingecheckt om ", scan.GetProperty("message").GetString(), StringComparison.Ordinal);

        var history = (await portal.GetFromJsonAsync<JsonElement>($"/api/v1/admin/members/{memberId}/access")).GetProperty("history");
        Assert.Equal(3, history.GetArrayLength());

        var (inactive, _) = await MemberAsync("oud@example.com", MembershipStatus.Inactive);
        var refused = await JsonAsync(await portal.PostAsJsonAsync($"/api/v1/admin/members/{inactive}/check-in", new { force = false }), HttpStatusCode.Conflict);
        Assert.Equal("Geen toegang: Geen actief lidmaatschap.", refused.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Zonder_activiteit_met_toegangscontrole_geen_scannen_en_een_gewoon_lid_mag_niet()
    {
        await EventAsync(accessControl: false);
        var future = await EventAsync();
        var deur = await DoorAsync("deur@example.com", "installatie-deur-een-0001");

        var status = await deur.GetFromJsonAsync<JsonElement>("/api/v1/access/status");
        Assert.Equal(JsonValueKind.Null, status.GetProperty("current").ValueKind);
        Assert.Equal(future, status.GetProperty("next").GetProperty("id").GetGuid());
        var closed = await JsonAsync(await deur.PostAsJsonAsync("/api/v1/access/scan", new { code = "X" }), HttpStatusCode.Conflict);
        Assert.Equal("ACCESS_NOT_ACTIVE", closed.GetProperty("code").GetString());

        var (_, lidOid) = await MemberAsync("lid@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lidOid).PostAsJsonAsync("/api/v1/access/scan", new { code = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deur.GetAsync("/api/v1/admin/access-scans/events")).StatusCode);
    }
}
