using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Qr;
using Drammers.Modules.Ticketing.Sales;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 19a: kaartverkoop — bestellen, Mollie, capaciteit, groepskaarten, munten, wachtlijst en het portal.</summary>
[Collection(SqlServerCollection.Name)]
public class SalesTests(SqlServerFixture sql) : IAsyncLifetime
{
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

    private async Task<Guid> ProductAsync(string kind, string name, int priceCents, int? capacity, string? date = null, int maxPerOrder = 10)
    {
        var id = await JsonAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/sales/products", new
        {
            kind,
            name,
            description = (string?)null,
            eventId = (Guid?)null,
            date,
            priceCents,
            capacity,
            maxPerOrder,
            saleOpensAt = (DateTime?)null,
            saleClosesAt = (DateTime?)null,
            onSale = true,
            sortOrder = 0,
        }), HttpStatusCode.Created);
        return id.GetGuid();
    }

    /// <summary>Een lid met account in een groep (vrij veld 3), plus extra actieve leden in dezelfde groep.</summary>
    private async Task<HttpClient> MemberAsync(string email, string group, int otherMembers = 0)
    {
        var (userId, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid);
        await WithDbAsync(async db =>
        {
            var memberId = IdGenerator.NewId();
            db.Members.Add(new Member { Id = memberId, MemberNumber = email[..5], FullName = $"Lid {email[..5]}", Email = email, ParadeGroupName = group, MembershipStatus = MembershipStatus.Active });
            for (var i = 0; i < otherMembers; i++)
            {
                db.Members.Add(new Member { Id = IdGenerator.NewId(), MemberNumber = $"{email[..3]}{i}", FullName = $"Groepslid {i}", ParadeGroupName = group, MembershipStatus = MembershipStatus.Active });
            }

            await db.SaveChangesAsync();
            return await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.MemberId, memberId));
        });
        return _api.ClientFor(oid);
    }

    private static object Order(Guid productId, int paid, int member = 0, string? name = "Jan Jansen", string? email = "jan@example.com") => new
    {
        productId,
        memberQuantity = member,
        paidQuantity = paid,
        buyerName = name,
        buyerEmail = email,
        buyerPhone = "0612345678",
        remark = (string?)null,
        channel = "Web",
    };

    private async Task WebhookAsync(string paymentId) =>
        Assert.Equal(HttpStatusCode.OK, (await _guest.PostAsync("/api/v1/payments/mollie/webhook", new FormUrlEncodedContent([new("id", paymentId)]))).StatusCode);

    [Fact]
    public async Task Gast_koopt_dagkaarten_betaalt_en_krijgt_een_QR_per_mail()
    {
        var product = await ProductAsync("DayTicket", "Dagkaart zaterdag", 750, capacity: 100, date: "2027-02-06");
        var created = await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/sales/orders", Order(product, paid: 2)), HttpStatusCode.Created);
        Assert.Equal("AwaitingPayment", created.GetProperty("status").GetString());
        Assert.StartsWith("https://mollie.test/checkout/", created.GetProperty("checkoutUrl").GetString());
        var payment = Assert.Single(_mollie.Created);
        Assert.Equal(1500, payment.AmountCents);
        var id = created.GetProperty("orderId").GetGuid();
        var token = created.GetProperty("token").GetString();
        var paymentId = _mollie.Payments.Keys.Single();

        // Een vervalste webhook (Mollie zegt nog "open") geeft geen kaarten.
        await WebhookAsync(paymentId);
        Assert.Equal(0, await WithDbAsync(db => db.OrderTickets.CountAsync()));

        _mollie.SetStatus(paymentId, "paid");
        await WebhookAsync(paymentId);
        await WebhookAsync(paymentId); // idempotent
        var order = await JsonAsync(await _guest.GetAsync($"/api/v1/sales/orders/{id}?t={token}"));
        Assert.Equal("Confirmed", order.GetProperty("status").GetString());
        var ticket = Assert.Single(order.GetProperty("tickets").EnumerateArray());
        Assert.Equal(2, ticket.GetProperty("quantity").GetInt32());
        var payload = QrPayload.TryDecode(ticket.GetProperty("code").GetString()!);
        Assert.Equal(QrPayload.OrderTicket, payload!.Version);
        Assert.Contains(_api.Emails.Sent, m => m.To == "jan@example.com" && m.Subject.Contains("Dagkaart zaterdag", StringComparison.Ordinal));

        // Zonder het juiste token geen inzage.
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync($"/api/v1/sales/orders/{id}?t=fout")).StatusCode);
        // Een onbekend id in de webhook: gewoon 200.
        await WebhookAsync("tr_onbekend");
    }

    [Fact]
    public async Task Nooit_meer_verkopen_dan_de_capaciteit()
    {
        var product = await ProductAsync("DayTicket", "Dagkaart maandag", 250, capacity: 10, date: "2027-02-08");
        var responses = await Task.WhenAll(Enumerable.Range(0, 25).Select(i =>
            _api.CreateClient().PostAsJsonAsync("/api/v1/sales/orders", Order(product, paid: 1, email: $"koper{i}@example.com"))));
        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(15, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // Een mislukte betaling geeft de plaats direct vrij.
        var paymentId = _mollie.Payments.Keys.First();
        _mollie.SetStatus(paymentId, "failed");
        await WebhookAsync(paymentId);
        await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/sales/orders", Order(product, paid: 1)), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict, (await _guest.PostAsJsonAsync("/api/v1/sales/orders", Order(product, paid: 1))).StatusCode);
    }

    [Fact]
    public async Task Groepskaarten_pronkzitting_gratis_tot_het_aantal_actieve_leden_over_beide_avonden()
    {
        var vrijdag = await ProductAsync("Pronkzitting", "Pronkzitting vrijdag", 1250, capacity: 300, date: "2027-02-05");
        var zaterdag = await ProductAsync("Pronkzitting", "Pronkzitting zaterdag", 1250, capacity: 300, date: "2027-02-06");
        var mendy = await MemberAsync("mendy@example.com", "Kruumels", otherMembers: 2);

        var catalog = await JsonAsync(await mendy.GetAsync("/api/v1/sales/products"));
        Assert.Equal(3, catalog.GetProperty("group").GetProperty("remaining").GetInt32());

        // 2 gratis groepskaarten + 1 losse kaart: alleen de losse kaart wordt betaald.
        var first = await JsonAsync(await mendy.PostAsJsonAsync("/api/v1/sales/orders", Order(vrijdag, paid: 1, member: 2, name: null, email: null)), HttpStatusCode.Created);
        Assert.Equal(1250, _mollie.Created.Single().AmountCents);
        _mollie.SetStatus(_mollie.Payments.Keys.Single(), "paid");
        await WebhookAsync(_mollie.Payments.Keys.Single());
        Assert.Equal("Confirmed", (await WithDbAsync(db => db.SaleOrders.SingleAsync(o => o.Id == first.GetProperty("orderId").GetGuid()))).Status.ToString());

        // Nog 1 gratis kaart over, ook op de andere avond.
        var tooMany = await mendy.PostAsJsonAsync("/api/v1/sales/orders", Order(zaterdag, paid: 0, member: 2, name: null, email: null));
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        var free = await JsonAsync(await mendy.PostAsJsonAsync("/api/v1/sales/orders", Order(zaterdag, paid: 0, member: 1, name: null, email: null)), HttpStatusCode.Created);
        Assert.Equal("Confirmed", free.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, free.GetProperty("checkoutUrl").ValueKind);

        // Een gast kan geen gratis groepskaarten bestellen.
        Assert.Equal(HttpStatusCode.Forbidden, (await _guest.PostAsJsonAsync("/api/v1/sales/orders", Order(vrijdag, paid: 0, member: 1))).StatusCode);

        // Het avondoverzicht en de export voor de tafelindeling.
        var evenings = await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/sales/pronkzitting"));
        var friday = evenings.EnumerateArray().First();
        var kruumels = Assert.Single(friday.GetProperty("rows").EnumerateArray());
        Assert.Equal("Kruumels", kruumels.GetProperty("name").GetString());
        Assert.Equal(3, kruumels.GetProperty("quantity").GetInt32());
        var export = await _bestuur.GetAsync("/api/v1/admin/sales/pronkzitting/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        using var workbook = new XLWorkbook(await export.Content.ReadAsStreamAsync());
        Assert.Equal(2, workbook.Worksheets.Count);
        var sheet = workbook.Worksheets.First();
        Assert.Equal("Groep / naam", sheet.Cell(1, 2).GetString());
        Assert.Equal("Kruumels", sheet.Cell(2, 2).GetString());
        Assert.Equal(3, sheet.Cell(2, 3).GetValue<int>());
    }

    [Fact]
    public async Task Munten_alleen_voor_leden()
    {
        var munten = await ProductAsync("Tokens", "Consumptiemunten", 250, capacity: null, maxPerOrder: 100);
        Assert.Equal(HttpStatusCode.Forbidden, (await _guest.PostAsJsonAsync("/api/v1/sales/orders", Order(munten, paid: 20))).StatusCode);
        var lid = await MemberAsync("piet@example.com", "Snotapen");
        var created = await JsonAsync(await lid.PostAsJsonAsync("/api/v1/sales/orders", Order(munten, paid: 20, name: null, email: null)), HttpStatusCode.Created);
        Assert.Equal(5000, _mollie.Created.Single().AmountCents);
        _mollie.SetStatus(_mollie.Payments.Keys.Single(), "paid");
        await WebhookAsync(_mollie.Payments.Keys.Single());

        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title == "Je kaarten staan klaar")));
        var mine = await JsonAsync(await lid.GetAsync("/api/v1/me/orders"));
        var order = Assert.Single(mine.EnumerateArray());
        Assert.Equal(created.GetProperty("number").GetString(), order.GetProperty("number").GetString());
        // Munten gaan via de munten-QR, niet via een QR bij de bestelling.
        Assert.Equal(JsonValueKind.Null, order.GetProperty("tickets")[0].GetProperty("code").ValueKind);
        var tokens = await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/sales/tokens"));
        Assert.Equal(20, tokens[0].GetProperty("paidQuantity").GetInt32());
        Assert.Equal(20, (await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/sales/summary"))).GetProperty("tokensToCollect").GetInt32());
    }

    [Fact]
    public async Task Wachtlijst_bestuur_kent_toe_met_betaallink_en_boekt_contant()
    {
        var vrijdag = await ProductAsync("Pronkzitting", "Pronkzitting vrijdag", 1250, capacity: 2, date: "2027-02-05");
        await JsonAsync(await _bestuur.PostAsJsonAsync("/api/v1/admin/sales/orders", new
        {
            productId = vrijdag,
            groupName = (string?)null,
            memberQuantity = 0,
            paidQuantity = 2,
            buyerName = "Kees",
            buyerEmail = "kees@example.com",
            buyerPhone = (string?)null,
            remark = "Bij het podium",
            payment = "Cash",
        }), HttpStatusCode.Created);

        // Vol: bestellen kan niet, de wachtlijst wel.
        Assert.Equal(HttpStatusCode.Conflict, (await _guest.PostAsJsonAsync("/api/v1/sales/orders", Order(vrijdag, paid: 2))).StatusCode);
        await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/sales/waitlist", Order(vrijdag, paid: 2)), HttpStatusCode.Created);
        Assert.Contains(_api.Emails.Sent, m => m.Subject.StartsWith("Je staat op de wachtlijst", StringComparison.Ordinal));

        // Het bestuur annuleert de eerste bestelling (niet terugbetalen) en kent de plaatsen toe met een betaallink.
        var orders = await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/sales/orders?productId={vrijdag}"));
        var kees = orders.GetProperty("items")[0].GetProperty("id").GetGuid();
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/sales/orders/{kees}/cancel", new { reason = "Dubbel besteld" }), HttpStatusCode.NoContent);
        Assert.Equal(OrderTicketStatus.Cancelled, (await WithDbAsync(db => db.OrderTickets.SingleAsync())).Status);

        var waitlist = await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/sales/products/{vrijdag}/waitlist"));
        var entry = waitlist[0];
        Assert.True(entry.GetProperty("fits").GetBoolean());
        var granted = await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/sales/waitlist/{entry.GetProperty("id").GetGuid()}/grant", new { payment = "PaymentLink" }));
        Assert.Equal("AwaitingPayment", granted.GetProperty("status").GetString());
        var mail = _api.Emails.Sent.Last(m => m.Subject.StartsWith("Er is plek", StringComparison.Ordinal));
        Assert.Contains("/api/v1/sales/orders/", mail.PlainText, StringComparison.Ordinal);

        // De betaallink stuurt door naar Mollie.
        var link = mail.PlainText.Split(' ', '\n').Single(w => w.Contains("/pay?t=", StringComparison.Ordinal)).Trim();
        var noRedirect = _api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var pay = await noRedirect.GetAsync(new Uri(link).PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, pay.StatusCode);
        Assert.StartsWith("https://mollie.test/checkout/", pay.Headers.Location!.ToString());

        // Toch contant betaald aan de kassa van het bestuur.
        await JsonAsync(await _bestuur.PostAsync($"/api/v1/admin/sales/orders/{granted.GetProperty("id").GetGuid()}/paid-cash", null), HttpStatusCode.NoContent);
        Assert.Equal(WaitlistStatus.Granted, (await WithDbAsync(db => db.WaitlistEntries.SingleAsync())).Status);
        Assert.Contains(_api.Emails.Sent, m => m.Subject.StartsWith("Je bestelling", StringComparison.Ordinal) && m.To == "jan@example.com");
    }

    [Fact]
    public async Task Kaartverkoop_beheren_alleen_met_sale_manage()
    {
        var lid = await MemberAsync("ruby@example.com", "Kruumels");
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/sales/products")).StatusCode);
        var (_, kassaOid) = await _api.CreateUserAsync("kassa@example.com", DefaultRoles.Kassa);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(kassaOid).GetAsync("/api/v1/admin/sales/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _bestuur.GetAsync("/api/v1/admin/sales/groups")).StatusCode);
    }

    [Fact]
    public async Task Kaarten_delen_met_een_lid_van_dezelfde_groep()
    {
        var zaterdag = await ProductAsync("Pronkzitting", "Pronkzitting zaterdag", 1250, capacity: 300, date: "2027-02-06");
        var mendy = await MemberAsync("mendy@example.com", "Kruumels", otherMembers: 5);
        var ruby = await MemberAsync("ruby@example.com", "Kruumels");
        var piet = await MemberAsync("piet@example.com", "Snotapen");
        var created = await JsonAsync(await mendy.PostAsJsonAsync("/api/v1/sales/orders", Order(zaterdag, paid: 0, member: 7, name: null, email: null)), HttpStatusCode.Created);
        var mine = await JsonAsync(await mendy.GetAsync("/api/v1/me/orders"));
        var ticket = mine[0].GetProperty("tickets")[0];
        Assert.Equal(7, ticket.GetProperty("quantity").GetInt32());
        Assert.True(ticket.GetProperty("canShare").GetBoolean());
        var ticketId = ticket.GetProperty("id").GetGuid();

        var candidates = await JsonAsync(await mendy.GetAsync($"/api/v1/me/orders/tickets/{ticketId}/share-candidates"));
        var rubyCandidate = candidates.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "Lid ruby@");
        Assert.True(rubyCandidate.GetProperty("hasAccount").GetBoolean());
        Assert.DoesNotContain(candidates.EnumerateArray(), c => c.GetProperty("name").GetString() == "Lid piet@");
        var rubyId = rubyCandidate.GetProperty("memberId").GetGuid();
        var pietId = await WithDbAsync(db => db.Members.Where(m => m.Email == "piet@example.com").Select(m => m.Id).SingleAsync());

        // Niet met iemand uit een andere groep, en niet alle kaarten.
        Assert.Equal(HttpStatusCode.Conflict, (await mendy.PostAsJsonAsync($"/api/v1/me/orders/tickets/{ticketId}/share", new { memberId = pietId, quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await mendy.PostAsJsonAsync($"/api/v1/me/orders/tickets/{ticketId}/share", new { memberId = rubyId, quantity = 7 })).StatusCode);
        // Alleen de besteller kan delen.
        Assert.Equal(HttpStatusCode.NotFound, (await ruby.PostAsJsonAsync($"/api/v1/me/orders/tickets/{ticketId}/share", new { memberId = rubyId, quantity = 1 })).StatusCode);

        await JsonAsync(await mendy.PostAsJsonAsync($"/api/v1/me/orders/tickets/{ticketId}/share", new { memberId = rubyId, quantity = 1 }), HttpStatusCode.NoContent);
        mine = await JsonAsync(await mendy.GetAsync("/api/v1/me/orders"));
        var own = Assert.Single(mine[0].GetProperty("tickets").EnumerateArray());
        Assert.Equal(6, own.GetProperty("quantity").GetInt32());
        Assert.Equal(1, mine[0].GetProperty("sharedWith")[0].GetProperty("quantity").GetInt32());

        var rubys = await JsonAsync(await ruby.GetAsync("/api/v1/me/orders"));
        var received = Assert.Single(rubys.EnumerateArray());
        Assert.Equal("Lid mendy", received.GetProperty("sharedBy").GetString());
        var rubyTicket = Assert.Single(received.GetProperty("tickets").EnumerateArray());
        Assert.Equal(1, rubyTicket.GetProperty("quantity").GetInt32());
        Assert.False(rubyTicket.GetProperty("canShare").GetBoolean());
        Assert.NotEqual(own.GetProperty("code").GetString(), rubyTicket.GetProperty("code").GetString());
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title == "Er is een kaart met je gedeeld")));

        // De webpagina toont de QR van de besteller als SVG.
        var token = created.GetProperty("token").GetString();
        var svg = await _guest.GetAsync($"/api/v1/sales/orders/{created.GetProperty("orderId").GetGuid()}/tickets/{ticketId}/qr.svg?t={token}");
        Assert.Equal(HttpStatusCode.OK, svg.StatusCode);
        Assert.Equal("image/svg+xml", svg.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<svg", await svg.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync($"/api/v1/sales/orders/{created.GetProperty("orderId").GetGuid()}/tickets/{ticketId}/qr.svg?t=fout")).StatusCode);
    }

    [Fact]
    public async Task Groepsmaximum_telt_personen_tweepersoonslid_telt_twee()
    {
        await ProductAsync("Pronkzitting", "Pronkzitting vrijdag", 1250, capacity: 300, date: "2027-02-05");
        var mendy = await MemberAsync("mendy@example.com", "Kruumels");
        await WithDbAsync(async db =>
        {
            db.Members.AddRange(
                new Member { Id = IdGenerator.NewId(), MemberNumber = "K1", FullName = "Duo", ParadeGroupName = "Kruumels", MemberCategory = "Tweepersoonslid DVD", MembershipStatus = MembershipStatus.Active },
                new Member { Id = IdGenerator.NewId(), MemberNumber = "K2", FullName = "Danseres", ParadeGroupName = "Kruumels", MemberCategory = "Lidmaatschap dansgarde DVD", MembershipStatus = MembershipStatus.Active },
                new Member { Id = IdGenerator.NewId(), MemberNumber = "K3", FullName = "Oud-lid", ParadeGroupName = "Kruumels", MemberCategory = "Tweepersoonslid DVD", MembershipStatus = MembershipStatus.Inactive });
            return await db.SaveChangesAsync();
        });
        var group = (await JsonAsync(await mendy.GetAsync("/api/v1/sales/products"))).GetProperty("group");
        // Mendy (zonder soort: 1) + tweepersoonslid (2) + dansgarde (1); het inactieve lid telt niet.
        Assert.Equal(3, group.GetProperty("activeMembers").GetInt32());
        Assert.Equal(4, group.GetProperty("persons").GetInt32());
        Assert.Equal(4, group.GetProperty("remaining").GetInt32());
    }
}
