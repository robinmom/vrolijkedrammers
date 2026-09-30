using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Sales;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 19c: gekochte kaarten scannen bij de deur (alle personen tegelijk, alleen online) en munten uitgeven bij de kassa.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ScannerKassaTests(SqlServerFixture sql) : IAsyncLifetime
{
    /// <summary>Zondag 7 februari 2027, 20:00 UTC: tijdens carnaval.</summary>
    private static readonly DateTimeOffset Evening = new(2027, 2, 7, 20, 0, 0, TimeSpan.Zero);

    private readonly FakeMollie _mollie = new();
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private HttpClient _guest = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(),
            configure: services => services.AddSingleton<Drammers.Infrastructure.Payments.IMollieClient>(_mollie));
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        _guest = _api.CreateClient();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

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

    private async Task<Guid> ProductAsync(string kind, string name, string? date, int maxPerOrder = 10) =>
        (await JsonAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/sales/products", new
        {
            kind,
            name,
            description = (string?)null,
            eventId = (Guid?)null,
            date,
            priceCents = 750,
            capacity = (int?)null,
            maxPerOrder,
            saleOpensAt = (DateTime?)null,
            saleClosesAt = (DateTime?)null,
            onSale = true,
            sortOrder = 0,
        }), HttpStatusCode.Created)).GetGuid();

    /// <summary>Een gast koopt en betaalt; geeft de QR-code zoals de webpagina en de e-mail die tonen.</summary>
    private async Task<string> BoughtCodeAsync(Guid product, int quantity, string email)
    {
        var created = await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/sales/orders", new
        {
            productId = product,
            memberQuantity = 0,
            paidQuantity = quantity,
            buyerName = "Jan Jansen",
            buyerEmail = email,
            buyerPhone = (string?)null,
            remark = (string?)null,
            channel = "Web",
        }), HttpStatusCode.Created);
        var paymentId = _mollie.Payments.Values.Single(p => p.OrderId == created.GetProperty("orderId").GetGuid()).Id;
        _mollie.SetStatus(paymentId, "paid");
        await _guest.PostAsync("/api/v1/payments/mollie/webhook", new FormUrlEncodedContent([new("id", paymentId)]));
        var order = await JsonAsync(await _guest.GetAsync($"/api/v1/sales/orders/{created.GetProperty("orderId").GetGuid()}?t={created.GetProperty("token").GetString()}"));
        return order.GetProperty("tickets")[0].GetProperty("code").GetString()!;
    }

    private async Task<HttpClient> OperatorAsync(string email, string role, string installationId)
    {
        var (_, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid, role);
        var client = _api.ClientFor(oid);
        client.DefaultRequestHeaders.Add("X-Device-Id", installationId);
        await JsonAsync(await client.PostAsJsonAsync("/api/v1/me/devices", new { installationId, platform = "Android", model = "Pixel 8", appVersion = "1.0.0" }));
        return client;
    }

    [Fact]
    public async Task Gekochte_kaart_alle_personen_tegelijk_binnen_daarna_geblokkeerd()
    {
        var zondag = await ProductAsync("DayTicket", "Dagkaart zondag", "2027-02-07");
        var maandag = await ProductAsync("DayTicket", "Dagkaart maandag", "2027-02-08");
        var code = await BoughtCodeAsync(zondag, 3, "jan@example.com");
        var other = await BoughtCodeAsync(maandag, 2, "kees@example.com");
        var queued = await BoughtCodeAsync(zondag, 1, "piet@example.com");
        var door = await OperatorAsync("deur@example.com", DefaultRoles.Scanner, "installatie-deur-0001");
        _api.Clock.Advance(Evening - _api.Clock.UtcNow);

        var first = await JsonAsync(await door.PostAsJsonAsync("/api/v1/access/scan", new { code }));
        Assert.Equal("Admitted", first.GetProperty("outcome").GetString());
        Assert.Equal(3, first.GetProperty("persons").GetInt32());
        Assert.Equal("Jan Jansen", first.GetProperty("holderName").GetString());
        Assert.StartsWith("3 personen tegelijk naar binnen", first.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(3, first.GetProperty("counts").GetProperty("inside").GetInt32());

        var again = await JsonAsync(await door.PostAsJsonAsync("/api/v1/access/scan", new { code }));
        Assert.Equal("Refused", again.GetProperty("outcome").GetString());
        Assert.Contains("AlreadyUsed", await ReasonsAsync());
        Assert.StartsWith("Al gescand om", again.GetProperty("message").GetString(), StringComparison.Ordinal);

        var wrongDay = await JsonAsync(await door.PostAsJsonAsync("/api/v1/access/scan", new { code = other }));
        Assert.Equal("Refused", wrongDay.GetProperty("outcome").GetString());
        Assert.Contains("Dagkaart maandag", wrongDay.GetProperty("message").GetString(), StringComparison.Ordinal);

        // Offline gescand: telt niet en de QR blijft geldig (alleen online).
        await JsonAsync(await door.PostAsJsonAsync("/api/v1/access/offline-scans", new
        {
            scans = new[] { new { clientScanId = Guid.NewGuid(), code = queued, scannedAt = Evening.UtcDateTime.AddMinutes(-5), localOutcome = "Refused" } },
        }));
        Assert.Contains("OnlineOnly", await ReasonsAsync());
        Assert.Equal("Admitted", (await JsonAsync(await door.PostAsJsonAsync("/api/v1/access/scan", new { code = queued }))).GetProperty("outcome").GetString());
        Assert.Equal(2, await WithDbAsync(db => db.OrderTickets.CountAsync(t => t.Status == OrderTicketStatus.Used)));
    }

    private Task<List<string?>> ReasonsAsync() => WithDbAsync(db => db.AccessScans.Select(s => s.Reason).ToListAsync());

    [Fact]
    public async Task Kassa_scant_munten_QR_toont_bestelling_en_geeft_een_keer_uit()
    {
        var munten = await ProductAsync("Tokens", "Consumptiemunten", null, maxPerOrder: 100);
        var (userId, oid) = await _api.CreateUserAsync("mendy@example.com", DefaultRoles.Lid);
        await WithDbAsync(async db =>
        {
            var memberId = IdGenerator.NewId();
            db.Members.Add(new Member { Id = memberId, MemberNumber = "M1", FullName = "Mendy Mom", Email = "mendy@example.com", ParadeGroupName = "Kruumels", MembershipStatus = MembershipStatus.Active });
            await db.SaveChangesAsync();
            return await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(x => x.SetProperty(u => u.MemberId, memberId));
        });
        var phone = _api.ClientFor(oid);
        phone.DefaultRequestHeaders.Add("X-Device-Id", "installatie-mendy-0001");
        await JsonAsync(await phone.PostAsJsonAsync("/api/v1/me/devices", new { installationId = "installatie-mendy-0001", platform = "Ios", model = "iPhone 15", appVersion = "1.0.0" }));
        await JsonAsync(await phone.GetAsync("/api/v1/me/ticket"));
        await JsonAsync(await phone.PostAsJsonAsync("/api/v1/me/ticket/bind-device", new { challenge = (string?)null, signature = (string?)null }), HttpStatusCode.NoContent);
        await JsonAsync(await phone.PostAsJsonAsync("/api/v1/sales/orders", new
        {
            productId = munten,
            memberQuantity = 0,
            paidQuantity = 20,
            buyerName = (string?)null,
            buyerEmail = (string?)null,
            buyerPhone = (string?)null,
            remark = (string?)null,
            channel = "App",
        }), HttpStatusCode.Created);
        var paymentId = _mollie.Payments.Keys.Single();
        _mollie.SetStatus(paymentId, "paid");
        await _guest.PostAsync("/api/v1/payments/mollie/webhook", new FormUrlEncodedContent([new("id", paymentId)]));
        var ticketId = (await JsonAsync(await phone.GetAsync("/api/v1/me/orders")))[0].GetProperty("tickets")[0].GetProperty("id").GetGuid();
        _api.Clock.Advance(Evening - _api.Clock.UtcNow);
        async Task<string> CodeAsync() =>
            (await JsonAsync(await phone.GetAsync($"/api/v1/me/ticket/code?purpose=Tokens&orderTicketId={ticketId}"))).GetProperty("code").GetString()!;

        // Deurcontrole mag niet bij de kassa; de munten-QR werkt niet bij de deur.
        var door = await OperatorAsync("deur@example.com", DefaultRoles.Scanner, "installatie-deur-0001");
        Assert.Equal(HttpStatusCode.Forbidden, (await door.PostAsJsonAsync("/api/v1/kassa/scan", new { code = await CodeAsync() })).StatusCode);

        var kassa = await OperatorAsync("kassa@example.com", DefaultRoles.Kassa, "installatie-kassa-0001");
        var code = await CodeAsync();
        var scan = await JsonAsync(await kassa.PostAsJsonAsync("/api/v1/kassa/scan", new { code }));
        Assert.Equal(("Ready", "Mendy Mom", 20, "iDEAL"),
            (scan.GetProperty("outcome").GetString(), scan.GetProperty("holderName").GetString(), scan.GetProperty("quantity").GetInt32(), scan.GetProperty("paidWith").GetString()));
        var scanId = scan.GetProperty("scanId").GetGuid();

        var issued = await JsonAsync(await kassa.PostAsync($"/api/v1/kassa/scans/{scanId}/issue", null));
        Assert.Equal("Issued", issued.GetProperty("outcome").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await kassa.PostAsync($"/api/v1/kassa/scans/{scanId}/issue", null)).StatusCode);

        // Na uitgeven maakt de app geen munten-QR meer voor deze bestelling; dezelfde code nog een keer scannen: rood.
        Assert.Equal(HttpStatusCode.Conflict, (await phone.GetAsync($"/api/v1/me/ticket/code?purpose=Tokens&orderTicketId={ticketId}")).StatusCode);
        var twice = await JsonAsync(await kassa.PostAsJsonAsync("/api/v1/kassa/scan", new { code }));
        Assert.Equal("Refused", twice.GetProperty("outcome").GetString());
        Assert.StartsWith("Deze munten zijn al uitgegeven om", twice.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, twice.GetProperty("scanId").ValueKind);

        var log = await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/sales/kassalog?day=2027-02-07"));
        Assert.Equal((1, 20, 1), (log.GetProperty("issued").GetInt32(), log.GetProperty("tokensIssued").GetInt32(), log.GetProperty("refused").GetInt32()));
        var rows = log.GetProperty("rows").EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("outcome").GetString() == "Issued" && r.GetProperty("memberName").GetString() == "Mendy Mom"
            && r.GetProperty("operatorName").GetString() == "kassa@example.com" && r.GetProperty("deviceName").GetString() == "Pixel 8");
        Assert.Contains(rows, r => r.GetProperty("reason").GetString() == "AlreadyIssued");
        Assert.Equal(HttpStatusCode.Forbidden, (await kassa.GetAsync("/api/v1/admin/sales/kassalog")).StatusCode);
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "tokens.issued")));
    }
}
