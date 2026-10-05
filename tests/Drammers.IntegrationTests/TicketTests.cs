using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Infrastructure.Ticketing;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Qr;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 13a (ADR-005): ledentickets, hardwaresleutel met proof-of-possession, fallbackcode en ticketbeheer.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class TicketTests(SqlServerFixture sql) : IAsyncLifetime, IDisposable
{
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

    /// <summary>Lid met account; per toestel een eigen client met <c>X-Device-Id</c>.</summary>
    private async Task<(Guid MemberId, string ObjectId)> MemberAsync(string email, MembershipStatus status = MembershipStatus.Active)
    {
        var (userId, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid);
        var memberId = IdGenerator.NewId();
        await WithDbAsync(async db =>
        {
            db.Members.Add(new Member { Id = memberId, MemberNumber = email[..3], FullName = "Piet Lid", MembershipStatus = status });
            await db.SaveChangesAsync();
            (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = memberId;
            return await db.SaveChangesAsync();
        });
        return (memberId, oid);
    }

    private async Task<HttpClient> DeviceAsync(string objectId, string installationId, string model = "iPhone 15")
    {
        var client = _api.ClientFor(objectId);
        client.DefaultRequestHeaders.Add("X-Device-Id", installationId);
        await JsonAsync(await client.PostAsJsonAsync("/api/v1/me/devices", new { installationId, platform = "Ios", model, appVersion = "1.0.0" }));
        return client;
    }

    private async Task<ECDsa> RegisterKeyAsync(HttpClient device, string level = "SecureEnclave")
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _keys.Add(key);
        await JsonAsync(await device.PutAsJsonAsync("/api/v1/me/devices/current/key",
            new { publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), securityLevel = level }), HttpStatusCode.NoContent);
        return key;
    }

    private static async Task<HttpResponseMessage> BindAsync(HttpClient device, ECDsa? key)
    {
        if (key is null)
        {
            return await device.PostAsJsonAsync("/api/v1/me/ticket/bind-device", new { challenge = (string?)null, signature = (string?)null });
        }

        var challenge = (await JsonAsync(await device.PostAsync("/api/v1/me/ticket/challenge", null))).GetProperty("challenge").GetString()!;
        var signature = key.SignData(Convert.FromBase64String(challenge), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return await device.PostAsJsonAsync("/api/v1/me/ticket/bind-device", new { challenge, signature = Convert.ToBase64String(signature) });
    }

    /// <summary>Wat de app doet: de code zelf ondertekenen met de hardwaresleutel.</summary>
    private static string DeviceCode(JsonElement ticket, ECDsa key, DateTimeOffset now)
    {
        var unsigned = QrPayload.Unsigned(QrPayload.DeviceSigned, Convert.FromBase64String(ticket.GetProperty("publicRef").GetString()!),
            ticket.GetProperty("credentialVersion").GetInt32(), Convert.FromBase64String(ticket.GetProperty("deviceShortId").GetString()!),
            now.ToUnixTimeSeconds(), QrPayload.DefaultValidFor);
        return Base45.Encode([.. unsigned, .. key.SignData(unsigned, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)]);
    }

    private async Task<QrValidation> ValidateAsync(string code)
    {
        using var scope = _api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TicketValidation>().ValidateAsync(code, default);
    }

    /// <summary>Naar zondag 7 februari 2027, 21:00 (carnaval 6–9 februari).</summary>
    private void ToCarnival() => _api.Clock.Advance(new DateTimeOffset(2027, 2, 7, 20, 0, 0, TimeSpan.Zero) - _api.Clock.UtcNow);

    private static Task<JsonElement> TicketAsync(HttpClient device) => device.GetFromJsonAsync<JsonElement>("/api/v1/me/ticket");

    [Fact]
    public async Task Actief_lid_krijgt_een_ticket_inactief_lid_niet()
    {
        var (memberId, oid) = await MemberAsync("act@example.com");
        var phone = await DeviceAsync(oid, "installatie-telefoon-0001");

        var ticket = await TicketAsync(phone);
        Assert.Equal("NotYetValid", ticket.GetProperty("state").GetString());
        Assert.Equal(16, Convert.FromBase64String(ticket.GetProperty("publicRef").GetString()!).Length);
        await TicketAsync(phone);
        Assert.Equal(1, await WithDbAsync(db => db.Tickets.CountAsync(t => t.MemberId == memberId)));

        var (inactiveId, inactiveOid) = await MemberAsync("ina@example.com", MembershipStatus.Inactive);
        var inactivePhone = await DeviceAsync(inactiveOid, "installatie-telefoon-0002");
        var none = await TicketAsync(inactivePhone);
        Assert.Equal("None", none.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("publicRef").ValueKind);
        Assert.False(await WithDbAsync(db => db.Tickets.AnyAsync(t => t.MemberId == inactiveId)));

        // Lokaal op actief gezet (bijv. aangemeld via "lid worden", niet in e-Boekhouden): dan wel een ticket.
        await WithDbAsync(async db =>
        {
            (await db.Members.SingleAsync(m => m.Id == inactiveId)).LocalStatusOverride = MembershipStatus.Active;
            return await db.SaveChangesAsync();
        });
        Assert.Equal("NotYetValid", (await TicketAsync(inactivePhone)).GetProperty("state").GetString());
        Assert.True(await WithDbAsync(db => db.Tickets.AnyAsync(t => t.MemberId == inactiveId)));
    }

    [Fact]
    public async Task Hardwaresleutel_koppelen_code_geldig_ander_toestel_maakt_oude_codes_ongeldig_en_blokkeren()
    {
        var (_, oid) = await MemberAsync("hw@example.com");
        var phone = await DeviceAsync(oid, "installatie-telefoon-0001");
        var phoneKey = await RegisterKeyAsync(phone);

        using (var wrong = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await BindAsync(phone, wrong)).StatusCode);
        }

        await JsonAsync(await BindAsync(phone, phoneKey), HttpStatusCode.NoContent);
        ToCarnival();
        var ticket = await TicketAsync(phone);
        Assert.Equal(("Valid", true, true), (ticket.GetProperty("state").GetString(), ticket.GetProperty("boundToThisDevice").GetBoolean(), ticket.GetProperty("deviceHasHardwareKey").GetBoolean()));
        var phoneCode = DeviceCode(ticket, phoneKey, _api.Clock.UtcNow);
        var valid = await ValidateAsync(phoneCode);
        Assert.Equal((QrCheck.Valid, "Piet Lid"), (valid.Result, valid.Ticket!.HolderName));
        Assert.Equal(HttpStatusCode.Conflict, (await phone.GetAsync("/api/v1/me/ticket/code")).StatusCode);

        // Afgemeld toestel: codes zijn meteen ongeldig.
        var phoneId = await WithDbAsync(db => db.Devices.Where(d => d.InstallationId == "installatie-telefoon-0001").Select(d => d.Id).SingleAsync());
        await WithDbAsync(db => db.Devices.Where(d => d.Id == phoneId).ExecuteUpdateAsync(u => u.SetProperty(d => d.Status, Modules.Identity.Devices.DeviceStatus.Revoked)));
        Assert.Equal(QrCheck.WrongDevice, (await ValidateAsync(phoneCode)).Result);
        await WithDbAsync(db => db.Devices.Where(d => d.Id == phoneId).ExecuteUpdateAsync(u => u.SetProperty(d => d.Status, Modules.Identity.Devices.DeviceStatus.Active)));

        var tablet = await DeviceAsync(oid, "installatie-tablet-00002", "Pixel 8");
        Assert.False((await TicketAsync(tablet)).GetProperty("boundToThisDevice").GetBoolean());
        var tabletKey = await RegisterKeyAsync(tablet, "StrongBox");
        await JsonAsync(await BindAsync(tablet, tabletKey), HttpStatusCode.NoContent);
        var moved = await TicketAsync(tablet);
        Assert.Equal(2, moved.GetProperty("rebindsLeft").GetInt32());
        Assert.Equal(QrCheck.WrongDevice, (await ValidateAsync(phoneCode)).Result);
        Assert.Equal(QrCheck.Valid, (await ValidateAsync(DeviceCode(moved, tabletKey, _api.Clock.UtcNow))).Result);

        var id = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tickets")).GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PostAsJsonAsync($"/api/v1/admin/tickets/{id}/action", new { action = "Block" })).StatusCode);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/tickets/{id}/action", new { action = "Block", reason = "Telefoon gestolen" }), HttpStatusCode.NoContent);
        var blocked = await ValidateAsync(DeviceCode(moved, tabletKey, _api.Clock.UtcNow));
        Assert.Equal((QrCheck.Blocked, "Ticket geblokkeerd."), (blocked.Result, blocked.Message));
        Assert.Equal("Blocked", (await TicketAsync(tablet)).GetProperty("state").GetString());

        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/tickets/{id}/action", new { action = "Unblock" }), HttpStatusCode.NoContent);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/tickets/{id}/action", new { action = "Reissue" }), HttpStatusCode.NoContent);
        Assert.Equal(QrCheck.Reissued, (await ValidateAsync(DeviceCode(moved, tabletKey, _api.Clock.UtcNow))).Result);
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "ticket.block")));
    }


    [Fact]
    public async Task Zonder_hardwaresleutel_een_servercode_die_de_validatie_accepteert()
    {
        var (_, oid) = await MemberAsync("sw@example.com");
        var phone = await DeviceAsync(oid, "installatie-telefoon-0001");
        await JsonAsync(await BindAsync(phone, key: null), HttpStatusCode.NoContent);

        Assert.Equal(HttpStatusCode.Conflict, (await phone.GetAsync("/api/v1/me/ticket/code")).StatusCode);
        ToCarnival();
        var code = await JsonAsync(await phone.GetAsync("/api/v1/me/ticket/code"));
        var text = code.GetProperty("code").GetString()!;
        Assert.Equal(QrPayload.ServerSigned, QrPayload.TryDecode(text)!.Version);
        Assert.Equal(QrCheck.Valid, (await ValidateAsync(text)).Result);

        _api.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(QrCheck.Expired, (await ValidateAsync(text)).Result);
        Assert.Equal(1, await WithDbAsync(db => db.TicketSigningKeys.CountAsync()));
    }

    [Fact]
    public async Task Overzetten_kan_drie_keer_daarna_via_het_bestuur_en_uitgifte_is_idempotent()
    {
        var (_, oid) = await MemberAsync("limiet@example.com");
        for (var i = 0; i < 4; i++)
        {
            await JsonAsync(await BindAsync(await DeviceAsync(oid, $"installatie-toestel-{i:0000}"), key: null), HttpStatusCode.NoContent);
        }

        var fifth = await DeviceAsync(oid, "installatie-toestel-0005");
        var limit = await JsonAsync(await BindAsync(fifth, key: null), HttpStatusCode.Conflict);
        Assert.Equal("TICKET_REBIND_LIMIT", limit.GetProperty("code").GetString());

        var ticket = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/tickets")).GetProperty("items")[0];
        Assert.Equal(3, ticket.GetProperty("rebindCount").GetInt32());
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/tickets/{ticket.GetProperty("id").GetGuid()}/action", new { action = "ResetRebinds" }), HttpStatusCode.NoContent);
        await JsonAsync(await BindAsync(fifth, key: null), HttpStatusCode.NoContent);

        await MemberAsync("nog@example.com");
        Assert.Equal(1, (await JsonAsync(await _bestuur.PostAsync("/api/v1/admin/tickets/issue", null))).GetProperty("issued").GetInt32());
        Assert.Equal(0, (await JsonAsync(await _bestuur.PostAsync("/api/v1/admin/tickets/issue", null))).GetProperty("issued").GetInt32());

        var lid = _api.ClientFor((await _api.CreateUserAsync("gewoon@example.com", DefaultRoles.Lid)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/tickets")).StatusCode);
    }

    [Fact]
    public async Task Ouder_toont_de_QR_van_een_kind_op_de_eigen_telefoon_tot_het_kind_een_eigen_account_heeft()
    {
        var (_, parentOid) = await MemberAsync("ouder@example.com");
        var parentId = await WithDbAsync(db => db.Users.Where(u => u.ExternalObjectId == parentOid).Select(u => u.Id).SingleAsync());
        await WithDbAsync(db => db.Users.Where(u => u.Id == parentId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisplayName, "Robin Mom")));
        var childId = IdGenerator.NewId();
        var strangerChild = IdGenerator.NewId();
        await WithDbAsync(async db =>
        {
            db.Members.Add(new Member { Id = childId, MemberNumber = "1042", FullName = "Lot Mom", FirstName = "Lot", BirthDate = new DateOnly(2016, 5, 1), MembershipStatus = MembershipStatus.Active });
            db.Members.Add(new Member { Id = strangerChild, MemberNumber = "1043", FullName = "Ander Kind", BirthDate = new DateOnly(2016, 5, 1), MembershipStatus = MembershipStatus.Active });
            db.GuardianRelations.Add(new Modules.Membership.Guardians.GuardianRelation
            {
                Id = IdGenerator.NewId(),
                MemberId = childId,
                GuardianUserId = parentId,
                GuardianName = "Robin Mom",
                VerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
            return await db.SaveChangesAsync();
        });
        var ouderRole = await WithDbAsync(db => db.Roles.Where(r => r.Code == DefaultRoles.Ouder).Select(r => r.Id).SingleAsync());
        await WithDbAsync(async db =>
        {
            db.Set<Modules.Identity.Users.UserRole>().Add(new Modules.Identity.Users.UserRole { UserId = parentId, RoleId = ouderRole, AssignedAt = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        });

        var phone = await DeviceAsync(parentOid, "installatie-ouder-0001");
        var key = await RegisterKeyAsync(phone);
        var detail = await JsonAsync(await phone.GetAsync($"/api/v1/me/children/{childId}"));
        Assert.Equal("Lot Mom", detail.GetProperty("child").GetProperty("fullName").GetString());
        Assert.Equal(["Robin Mom"], detail.GetProperty("guardians").EnumerateArray().Select(g => g.GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync($"/api/v1/me/children/{strangerChild}/ticket")).StatusCode);

        // Eigen QR van de ouder en die van het kind op hetzelfde toestel.
        var challenge = (await JsonAsync(await phone.PostAsync($"/api/v1/me/children/{childId}/ticket/challenge", null))).GetProperty("challenge").GetString()!;
        var signature = key.SignData(Convert.FromBase64String(challenge), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        await JsonAsync(await phone.PostAsJsonAsync($"/api/v1/me/children/{childId}/ticket/bind-device", new { challenge, signature = Convert.ToBase64String(signature) }), HttpStatusCode.NoContent);
        await JsonAsync(await BindAsync(phone, key), HttpStatusCode.NoContent);
        ToCarnival();
        var childTicket = await JsonAsync(await phone.GetAsync($"/api/v1/me/children/{childId}/ticket"));
        Assert.Equal(("Valid", "Lot Mom", true), (childTicket.GetProperty("state").GetString(), childTicket.GetProperty("holderName").GetString(), childTicket.GetProperty("boundToThisDevice").GetBoolean()));
        var valid = await ValidateAsync(DeviceCode(childTicket, key, _api.Clock.UtcNow));
        Assert.Equal((QrCheck.Valid, "Lot Mom"), (valid.Result, valid.Ticket!.HolderName));
        var own = await TicketAsync(phone);
        Assert.Equal((QrCheck.Valid, "Piet Lid"), ((await ValidateAsync(DeviceCode(own, key, _api.Clock.UtcNow))).Result, (await ValidateAsync(DeviceCode(own, key, _api.Clock.UtcNow))).Ticket!.HolderName));

        // Eigen account voor het kind: de QR is niet meer bij de ouder; opnieuw koppelen telt niet mee.
        await WithDbAsync(db => db.Members.Where(m => m.Id == childId).ExecuteUpdateAsync(s => s.SetProperty(m => m.BirthDate, new DateOnly(2011, 1, 1))));
        Assert.Equal(HttpStatusCode.Accepted, (await _bestuur.PostAsJsonAsync($"/api/v1/admin/members/{childId}/own-account", new { email = "lot@example.com" })).StatusCode);
        foreach (var message in await WithDbAsync(db => db.Outbox.AsNoTracking().Where(m => m.Type == Drammers.Infrastructure.Identity.MemberAccounts.ProvisionMessageType).ToListAsync()))
        {
            using var scope = _api.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<Drammers.Infrastructure.Identity.MemberAccounts>().RunProvisioningAsync(
                JsonSerializer.Deserialize<Drammers.Infrastructure.Identity.MemberAccounts.ProvisionMessage>(message.Payload, JsonSerializerOptions.Web)!, default);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await phone.GetAsync($"/api/v1/me/children/{childId}/ticket")).StatusCode);
        Assert.Equal(QrCheck.WrongDevice, (await ValidateAsync(DeviceCode(childTicket, key, _api.Clock.UtcNow))).Result);
        var ticket = await WithDbAsync(db => db.Tickets.AsNoTracking().SingleAsync(t => t.MemberId == childId));
        Assert.Equal((null, 0), (ticket.BoundDeviceId, ticket.RebindCount));
        Assert.False(Assert.Single((await JsonAsync(await phone.GetAsync("/api/v1/me/children"))).EnumerateArray()).GetProperty("canShowQr").GetBoolean());
    }
}
