using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.Festival;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Dansgardefestival (2026-10-12): inschrijven, wijzigen via de link, beoordelen per dans, wachtlijst, betalen en de eigen rol.</summary>
[Collection(SqlServerCollection.Name)]
public class DanceFestivalTests(SqlServerFixture sql) : IAsyncLifetime
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
        _guest = _api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
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

    private Task<HttpResponseMessage> SaveSettingsAsync(int capacity = 2, bool payments = false, bool open = true) =>
        _bestuur.PutAsJsonAsync("/api/v1/admin/dance-festival/settings", new
        {
            name = "Dansgardefestival 2027",
            festivalDate = "2027-02-21",
            location = "’t Hart van Loil",
            pageEnabled = true,
            registrationOpensAt = open ? DateTime.UtcNow.AddDays(-1) : DateTime.UtcNow.AddDays(10),
            registrationClosesAt = DateTime.UtcNow.AddDays(20),
            capacity,
            paymentsEnabled = payments,
            priceSoloDuoCents = 1000,
            priceGroupOneDanceCents = 1750,
            priceGroupTwoDancesCents = 2750,
            vatPercent = 0,
            notificationEmail = "dansgarde@vrolijkedrammers.nl",
        });

    private static object Dance(string type, string format, string names, decimal age, int dancers, Guid? id = null) => new
    {
        id,
        danceType = type,
        format,
        names,
        averageAge = age,
        dancerCount = dancers,
        leaderName = "Sanne Jansen",
        title = type == "Show" ? "Onder de zee" : null,
        showStyle = type == "Show" ? "Character" : null,
    };

    private static object Registration(string club, object[] dances, bool? photo = true, bool consents = true, int elapsedMs = 20_000) => new
    {
        clubName = club,
        contactName = "Sanne Jansen",
        addressLine = "Dorpsstraat 12",
        postalCode = "6999 ab",
        city = "Zevenaar",
        phone = "0612345678",
        email = $"{club.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant()}@example.com",
        remarks = (string?)null,
        photoConsent = photo,
        privacyAccepted = consents,
        rulesAccepted = consents,
        entries = dances,
        website = (string?)null,
        elapsedMs,
    };

    /// <summary>Id en token uit de link in de bevestigingsmail.</summary>
    private (Guid Id, string Token) EditLink(string to)
    {
        var mail = _api.Emails.Sent.Last(m => m.To == to && m.Subject.StartsWith("Inschrijving Dansgardefestival", StringComparison.Ordinal));
        var link = new Uri(mail.PlainText.Split(' ', '\n').Single(w => w.Contains("inschrijving=", StringComparison.Ordinal)).Trim());
        var query = System.Web.HttpUtility.ParseQueryString(link.Query);
        return (Guid.Parse(query["inschrijving"]!), query["t"]!);
    }

    [Fact]
    public async Task Inschrijven_met_meerdere_dansen_bevestiging_en_melding_aan_de_organisatie()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync("/api/v1/dance-festival")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync("/dansgardefestival")).StatusCode);
        await JsonAsync(await SaveSettingsAsync());

        var info = await JsonAsync(await _guest.GetAsync("/api/v1/dance-festival"));
        Assert.Equal("Open", info.GetProperty("festival").GetProperty("state").GetString());
        Assert.Contains("Dansgardefestival", await _guest.GetStringAsync("/doe-mee"));
        Assert.Contains("js/forms/dansgardefestival.js", await _guest.GetStringAsync("/dansgardefestival"));

        var submitted = await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations", Registration("Knollenvreters",
        [
            Dance("Garde", "Group", "Garde De Knollen", 10.4m, 14),
            Dance("Show", "Group", "garde de knollen", 10.4m, 14),
            Dance("Show", "Duo", "Lotte en Fenna", 13.5m, 2),
        ])), HttpStatusCode.Accepted);
        Assert.Equal(1, submitted.GetProperty("number").GetInt32());

        var registration = await WithDbAsync(db => db.DanceFestivalRegistrations.Include(r => r.Entries).SingleAsync());
        Assert.Equal("6999 AB", registration.PostalCode);
        Assert.True(registration.PhotoConsent);
        Assert.Equal([DanceCategory.Junioren, DanceCategory.Junioren, DanceCategory.Jeugd], registration.Entries.OrderBy(e => e.SortOrder).Select(e => e.Category));
        Assert.All(registration.Entries, e => Assert.Equal(DanceEntryStatus.New, e.Status));

        var confirmation = Assert.Single(_api.Emails.Sent, m => m.To == "knollenvreters@example.com");
        Assert.Contains("inschrijfnummer is 1", confirmation.PlainText, StringComparison.Ordinal);
        // Groep met gardedans én showdans (€ 27,50) plus een duo (€ 10,00).
        Assert.Contains("37,50", confirmation.PlainText, StringComparison.Ordinal);
        Assert.Equal("dansgarde@vrolijkedrammers.nl", confirmation.ReplyTo);
        Assert.Single(_api.Emails.Sent, m => m.To == "dansgarde@vrolijkedrammers.nl" && m.Subject.StartsWith("Nieuwe inschrijving 1", StringComparison.Ordinal));

        // Zonder akkoord of zonder keuze voor foto's: geweigerd. Een bot (te snel) krijgt hetzelfde antwoord, zonder inschrijving.
        Assert.False((await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations",
            Registration("Zonder akkoord", [Dance("Garde", "Solo", "Noor", 9m, 1)], consents: false))).IsSuccessStatusCode);
        Assert.False((await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations",
            Registration("Zonder keuze", [Dance("Garde", "Solo", "Noor", 9m, 1)], photo: null))).IsSuccessStatusCode);
        Assert.False((await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations",
            Registration("Fout duo", [Dance("Garde", "Duo", "Noor", 9m, 3)]))).IsSuccessStatusCode);
        var bot = await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations",
            Registration("Bot", [Dance("Garde", "Solo", "Noor", 9m, 1)], elapsedMs: 500)), HttpStatusCode.Accepted);
        Assert.Equal(JsonValueKind.Null, bot.GetProperty("number").ValueKind);
        Assert.Equal(1, await WithDbAsync(db => db.DanceFestivalRegistrations.CountAsync()));
    }

    [Fact]
    public async Task Inschrijven_kan_alleen_in_de_inschrijfperiode()
    {
        await JsonAsync(await SaveSettingsAsync(open: false));
        Assert.Equal("NotYetOpen", (await JsonAsync(await _guest.GetAsync("/api/v1/dance-festival"))).GetProperty("festival").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations",
            Registration("Te vroeg", [Dance("Garde", "Solo", "Noor", 9m, 1)]))).StatusCode);
    }

    [Fact]
    public async Task Beoordelen_per_dans_met_plekken_wachtlijst_schuiven_en_wijzigen_na_goedkeuring()
    {
        await JsonAsync(await SaveSettingsAsync(capacity: 1));
        await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations", Registration("Eerste",
            [Dance("Garde", "Group", "Garde Eerste", 17m, 12), Dance("Show", "Solo", "Isa", 14m, 1)])), HttpStatusCode.Accepted);
        await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations", Registration("Tweede",
            [Dance("Garde", "Group", "Garde Tweede", 8m, 10)])), HttpStatusCode.Accepted);
        var first = await WithDbAsync(db => db.DanceFestivalRegistrations.Include(r => r.Entries).SingleAsync(r => r.Number == 1));
        var second = await WithDbAsync(db => db.DanceFestivalRegistrations.Include(r => r.Entries).SingleAsync(r => r.Number == 2));
        var group = first.Entries.Single(e => e.Format == DanceFormat.Group).Id;
        var solo = first.Entries.Single(e => e.Format == DanceFormat.Solo).Id;
        var other = second.Entries.Single().Id;

        // Per dans: de groep goedgekeurd (plek 1 van 1), de solo past niet meer.
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{first.Id}/decisions",
            new { entryIds = new[] { group }, decision = "Approve", message = (string?)null }), HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Conflict, (await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{first.Id}/decisions",
            new { entryIds = new[] { solo }, decision = "Approve" })).StatusCode);
        Assert.Contains(_api.Emails.Sent, m => m.To == "eerste@example.com" && m.Subject.EndsWith("goedgekeurd", StringComparison.Ordinal));

        // Wachtlijst: eerst de solo, dan de tweede vereniging; daarna schuift de tweede omhoog.
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{first.Id}/decisions",
            new { entryIds = new[] { solo }, decision = "Waitlist" }), HttpStatusCode.NoContent);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{second.Id}/decisions",
            new { entryIds = new[] { other }, decision = "Waitlist" }), HttpStatusCode.NoContent);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/entries/{other}/waitlist-move", new { direction = -1 }), HttpStatusCode.NoContent);
        var positions = await WithDbAsync(db => db.DanceFestivalEntries.Where(e => e.WaitlistPosition != null).ToDictionaryAsync(e => e.Id, e => e.WaitlistPosition));
        Assert.Equal(1, positions[other]);
        Assert.Equal(2, positions[solo]);

        // Meer informatie vragen vereist een vraag; de vereniging past aan via de link en de dans moet opnieuw beoordeeld worden.
        Assert.False((await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{first.Id}/decisions",
            new { entryIds = new[] { group }, decision = "RequestInfo" })).IsSuccessStatusCode);
        var (id, token) = EditLink("eerste@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync($"/api/v1/dance-festival/registrations/{id}?t=fout")).StatusCode);
        var own = await JsonAsync(await _guest.GetAsync($"/api/v1/dance-festival/registrations/{id}?t={token}"));
        Assert.True(own.GetProperty("editable").GetBoolean());
        await JsonAsync(await _guest.PutAsJsonAsync($"/api/v1/dance-festival/registrations/{id}?t={token}", Registration("Eerste",
            [Dance("Garde", "Group", "Garde Eerste", 17m, 13, group), Dance("Show", "Solo", "Isa", 14m, 1, solo), Dance("Show", "Duo", "Isa en Bo", 14.5m, 2)])),
            HttpStatusCode.NoContent);
        var entries = await WithDbAsync(db => db.DanceFestivalEntries.Where(e => e.RegistrationId == id).ToDictionaryAsync(e => e.Id));
        Assert.Equal(DanceEntryStatus.Changed, entries[group].Status);
        Assert.True(entries[group].HoldsPlace);
        Assert.Equal(DanceEntryStatus.Waitlisted, entries[solo].Status);
        Assert.Single(entries.Values, e => e.Status == DanceEntryStatus.New);
        Assert.Contains(_api.Emails.Sent, m => m.To == "dansgarde@vrolijkedrammers.nl" && m.Subject.StartsWith("Inschrijving 1 gewijzigd", StringComparison.Ordinal));

        // De vorige versie blijft bewaard en is in het portal te zien.
        var detail = await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/dance-festival/registrations/{id}"));
        Assert.Equal(2, detail.GetProperty("version").GetInt32());
        var previous = await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/dance-festival/registrations/{id}/versions/1"));
        Assert.Equal(12, previous.GetProperty("entries")[0].GetProperty("dancerCount").GetInt32());

        var list = await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/dance-festival/registrations"));
        Assert.Equal(1, list.GetProperty("stats").GetProperty("placesTaken").GetInt32());
        Assert.Equal(2, list.GetProperty("stats").GetProperty("waitlisted").GetInt32());
        var csv = await _bestuur.GetStringAsync("/api/v1/admin/dance-festival/export");
        Assert.Contains("Garde Eerste", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Betaallink_na_goedkeuring_en_status_via_Mollie()
    {
        await JsonAsync(await SaveSettingsAsync());
        await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations", Registration("Betalers",
            [Dance("Garde", "Group", "Garde Betalers", 12m, 9)])), HttpStatusCode.Accepted);
        var registration = await WithDbAsync(db => db.DanceFestivalRegistrations.Include(r => r.Entries).SingleAsync());
        var amount = new { amountCents = 1750 };

        // Betalen staat uit; en pas na goedkeuring.
        Assert.Equal(HttpStatusCode.Conflict, (await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}/payments", amount)).StatusCode);
        await JsonAsync(await SaveSettingsAsync(payments: true));
        Assert.False((await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}/payments", amount)).IsSuccessStatusCode);
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}/decisions",
            new { entryIds = registration.Entries.Select(e => e.Id), decision = "Approve" }), HttpStatusCode.NoContent);
        var detail = await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}"));
        Assert.Equal(1750, detail.GetProperty("suggestedAmountCents").GetInt32());

        var created = await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}/payments", amount), HttpStatusCode.Created);
        var paymentId = created.GetProperty("id").GetGuid();
        var mail = _api.Emails.Sent.Last(m => m.To == "betalers@example.com");
        Assert.Contains("17,50", mail.PlainText, StringComparison.Ordinal);
        var link = mail.PlainText.Split(' ', '\n').Single(w => w.Contains("/pay?t=", StringComparison.Ordinal)).Trim();

        var pay = await _guest.GetAsync(new Uri(link).PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, pay.StatusCode);
        Assert.StartsWith("https://mollie.test/checkout/", pay.Headers.Location!.ToString(), StringComparison.Ordinal);
        var mollieId = _mollie.Payments.Single().Key;
        Assert.Equal(paymentId, _mollie.Payments[mollieId].OrderId);

        // Nog een keer op de link terwijl de betaling open staat: dezelfde betaalpagina, geen tweede betaling.
        Assert.Equal(pay.Headers.Location, (await _guest.GetAsync(new Uri(link).PathAndQuery)).Headers.Location);
        Assert.Single(_mollie.Created);

        // Verlopen bij Mollie: een nieuwe betaling. Die wordt betaald; de webhook haalt de status bij Mollie op.
        _mollie.SetStatus(mollieId, "expired");
        Assert.Equal(HttpStatusCode.Redirect, (await _guest.GetAsync(new Uri(link).PathAndQuery)).StatusCode);
        var secondId = _mollie.Payments.Keys.Single(k => k != mollieId);
        _mollie.SetStatus(secondId, "paid");
        Assert.Equal(HttpStatusCode.OK, (await _guest.PostAsync("/api/v1/payments/mollie/webhook", new FormUrlEncodedContent([new("id", secondId)]))).StatusCode);
        var payment = await WithDbAsync(db => db.DanceFestivalPayments.SingleAsync());
        Assert.Equal(DanceFestivalPaymentStatus.Paid, payment.Status);
        Assert.Equal(0, (await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}"))).GetProperty("suggestedAmountCents").GetInt32());

        // Nog een keer op de link: al betaald, terug naar de pagina met een vaste meldingscode (geen vrije tekst).
        var again = await _guest.GetAsync(new Uri(link).PathAndQuery);
        Assert.EndsWith("/dansgardefestival/?melding=betaald", again.Headers.Location!.ToString(), StringComparison.Ordinal);

        // De eerste (oude) betaling wordt toch nog betaald: herkend, niet nog eens geboekt, maar gemarkeerd om terug te betalen.
        _mollie.SetStatus(mollieId, "paid");
        Assert.Equal(HttpStatusCode.OK, (await _guest.PostAsync("/api/v1/payments/mollie/webhook", new FormUrlEncodedContent([new("id", mollieId)]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _guest.PostAsync("/api/v1/payments/mollie/webhook", new FormUrlEncodedContent([new("id", mollieId)]))).StatusCode);
        detail = await JsonAsync(await _bestuur.GetAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}"));
        Assert.Equal(1, detail.GetProperty("payments")[0].GetProperty("duplicatePaid").GetInt32());
        var list = await JsonAsync(await _bestuur.GetAsync("/api/v1/admin/dance-festival/registrations"));
        Assert.Equal("Dubbel betaald", list.GetProperty("registrations")[0].GetProperty("paymentStatus").GetString());
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "dance-festival.payment-paid-twice")));
    }

    [Fact]
    public async Task Btw_en_servicekosten_op_het_festival_betaalverzoek()
    {
        // Btw 21% en servicekosten van € 0,35 (+21%); het ingevulde bedrag is exclusief btw.
        var saved = await JsonAsync(await _bestuur.PutAsJsonAsync("/api/v1/admin/dance-festival/settings", new
        {
            name = "Dansgardefestival 2027",
            festivalDate = "2027-02-21",
            location = "’t Hart van Loil",
            pageEnabled = true,
            registrationOpensAt = DateTime.UtcNow.AddDays(-1),
            registrationClosesAt = DateTime.UtcNow.AddDays(20),
            capacity = 5,
            paymentsEnabled = true,
            priceSoloDuoCents = 1000,
            priceGroupOneDanceCents = 1750,
            priceGroupTwoDancesCents = 2750,
            vatPercent = 21,
            serviceFeeOn = true,
            serviceFeeCents = 35,
            notificationEmail = "dansgarde@vrolijkedrammers.nl",
        }));
        Assert.Equal((21, true, 35), (saved.GetProperty("vatPercent").GetInt32(), saved.GetProperty("serviceFeeOn").GetBoolean(),
            saved.GetProperty("serviceFeeCents").GetInt32()));

        await JsonAsync(await _guest.PostAsJsonAsync("/api/v1/dance-festival/registrations",
            Registration("Btw", [Dance("Garde", "Group", "Garde Btw", 12m, 9)])), HttpStatusCode.Accepted);
        var registration = await WithDbAsync(db => db.DanceFestivalRegistrations.Include(r => r.Entries).SingleAsync());
        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}/decisions",
            new { entryIds = registration.Entries.Select(e => e.Id), decision = "Approve" }), HttpStatusCode.NoContent);

        await JsonAsync(await _bestuur.PostAsJsonAsync($"/api/v1/admin/dance-festival/registrations/{registration.Id}/payments",
            new { amountCents = 1750 }), HttpStatusCode.Created);

        var payment = await WithDbAsync(db => db.DanceFestivalPayments.SingleAsync());
        // netto 1750 + btw 368 (21%) + servicekosten 35 + btw 7 = 2160 totaal.
        Assert.Equal((1750, 368, 35, 7, 2160),
            (payment.NetCents, payment.VatCents, payment.ServiceFeeNetCents, payment.ServiceFeeVatCents, payment.AmountCents));
        Assert.StartsWith("FEST-2027-", payment.InvoiceNumber, StringComparison.Ordinal);
        var payMail = _api.Emails.Sent.Last(m => m.Subject.Contains("betaallink", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("21,60", payMail.PlainText, StringComparison.Ordinal);
        // Fase 2b: de factuur-PDF zit als bijlage bij de betaalmail.
        var attachment = Assert.Single(payMail.Attachments!);
        Assert.StartsWith("Factuur FEST-2027-", attachment.Name, StringComparison.Ordinal);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(attachment.Content, 0, 4));
    }

    [Fact]
    public async Task De_rol_Dansgardefestival_ziet_alleen_het_festival()
    {
        var (_, objectId) = await _api.CreateUserAsync("chantal@example.com", DefaultRoles.Dansgardefestival);
        var chantal = _api.ClientFor(objectId);
        Assert.Equal(HttpStatusCode.NoContent, (await chantal.GetAsync("/api/v1/admin/dance-festival/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await chantal.GetAsync("/api/v1/admin/dance-festival/registrations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await chantal.GetAsync("/api/v1/admin/members")).StatusCode);
        var lid = _api.ClientFor((await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/dance-festival/registrations")).StatusCode);
    }
}
