using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Drammers.Infrastructure.Mailings;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Groups;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Notification.Mailing;
using Drammers.SharedKernel.Identifiers;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 27a: mailinggroepen, een mailing opstellen, voorbeeld, versturen in delen en afmelden.</summary>
[Collection(SqlServerCollection.Name)]
public partial class MailingTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync(),
            services => services.PostConfigure<MailingOptions>(o => (o.PublicBaseUrl, o.MaxPerHour) = ("https://dvd.test", 60)));
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> MemberAsync(string number, string fullName, string? firstName, string? email, MembershipStatus status = MembershipStatus.Active)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var member = new Member { Id = IdGenerator.NewId(), MemberNumber = number, FullName = fullName, FirstName = firstName, Email = email, MembershipStatus = status };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private async Task<Guid> GroupAsync(string name, params Guid[] members)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var group = new Modules.Membership.Groups.Group { Id = IdGenerator.NewId(), Name = name, Type = GroupType.Committee };
        group.Memberships.AddRange(members.Select(m => new GroupMembership { GroupId = group.Id, MemberId = m }));
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group.Id;
    }

    private static object MailingBody(Guid listId, string subject = "Uitnodiging pronkzitting") => new
    {
        kind = "Invitation",
        subject,
        preheader = "Zaterdag 6 februari",
        listIds = new[] { listId },
        blocks = new object[]
        {
            new { type = "heading", text = "Je bent uitgenodigd, {voornaam}!" },
            new { type = "text", text = "Beste {voornaam},\n\nKom naar de **pronkzitting**. Meer op [de website](https://www.vrolijkedrammers.nl)." },
            new { type = "highlight", label = "Datum", text = "Zaterdag 6 februari 2027", note = "Zaal open om 19.00 uur" },
            new { type = "button", label = "Bestel kaarten", url = "https://www.vrolijkedrammers.nl/kaarten/" },
            new { type = "divider" },
        },
    };

    private async Task RunOutboxAsync()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var messages = await db.Outbox.AsNoTracking().Where(m => m.ProcessedAt == null && m.Type == MailingService.MailMessageType).OrderBy(m => m.CreatedAt).ToListAsync();
        foreach (var message in messages)
        {
            using var handlerScope = _api.Services.CreateScope();
            var handler = handlerScope.ServiceProvider.GetServices<IOutboxMessageHandler>().Single(h => h.Type == message.Type);
            await handler.HandleAsync(new OutboxEnvelope(message.Id, message.Type, message.Payload, 0), CancellationToken.None);
            await db.Outbox.Where(m => m.Id == message.Id).ExecuteUpdateAsync(x => x.SetProperty(m => m.ProcessedAt, DateTime.UtcNow));
        }
    }

    [Fact]
    public async Task Groep_opstellen_voorbeeld_versturen_in_delen_en_afmelden()
    {
        var piet = await MemberAsync("0101", "Piet van der Lid", "Piet", "Piet@Example.com");
        await MemberAsync("0102", "Zonder Mail", "Zonder", null);
        var anna = await MemberAsync("0103", "Anna de Raad", "Anna", "anna@example.com");
        await MemberAsync("0104", "Oud Lid", "Oud", "oud@example.com", MembershipStatus.Inactive);
        var raad = await GroupAsync("Raad van Elf (test)", anna);

        // Een groep met een los lid, een ledengroep en losse adressen (één dubbel met een lid, één ongeldig wordt geweigerd).
        var invalid = await _bestuur.PostAsJsonAsync("/api/v1/admin/mailing/lists", new
        {
            name = "Pronkzitting gasten",
            description = (string?)null,
            allMembers = false,
            memberIds = Array.Empty<Guid>(),
            groupIds = Array.Empty<Guid>(),
            addresses = new[] { new { email = "geen-adres", name = (string?)null } },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/mailing/lists", new
        {
            name = "Pronkzitting gasten",
            description = "Genodigden 2027",
            allMembers = false,
            memberIds = new[] { piet },
            groupIds = new[] { raad },
            addresses = new[]
            {
                new { email = "gast@example.com", name = (string?)"Gerda Gast" },
                new { email = "piet@example.com", name = (string?)null },
                new { email = "weg@example.com", name = (string?)null },
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var listId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            db.MailingUnsubscribes.Add(new MailingUnsubscribe { Email = "weg@example.com", UnsubscribedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var list = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/mailing/lists/{listId}");
        var audience = list.GetProperty("audience");
        Assert.Equal((3, 1), (audience.GetProperty("recipients").GetInt32(), audience.GetProperty("unsubscribed").GetInt32()));

        // Voorbeeld: ingevulde aanhef, knop en het aantal ontvangers; een kapotte knop geeft een duidelijke fout.
        var preview = await (await _bestuur.PostAsJsonAsync("/api/v1/admin/mailing/preview", MailingBody(listId))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Beste Piet,", preview.GetProperty("html").GetString());
        Assert.Contains("Bestel kaarten", preview.GetProperty("html").GetString());
        Assert.Contains("de website (https://www.vrolijkedrammers.nl)", preview.GetProperty("plainText").GetString());
        Assert.Equal(3, preview.GetProperty("audience").GetProperty("recipients").GetInt32());

        // Het voorbeeld op een eigen adres, met een CSP die de opmaak van de mail toestaat (het portal zelf niet).
        var previewUrl = preview.GetProperty("previewUrl").GetString()!;
        var document = await _api.CreateClient().GetAsync(previewUrl);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Contains("Beste Piet,", await document.Content.ReadAsStringAsync());
        var csp = document.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("frame-ancestors 'self'", csp);
        Assert.DoesNotContain("script-src", csp);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.CreateClient().GetAsync("/mailing-voorbeeld/00000000000000000000000000000000")).StatusCode);
        var broken = await _bestuur.PostAsJsonAsync("/api/v1/admin/mailing/preview", new
        {
            kind = "Newsletter",
            subject = "Test",
            preheader = (string?)null,
            listIds = Array.Empty<Guid>(),
            blocks = new object[] { new { type = "button", label = "Klik", url = "http://onveilig.test" } },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, broken.StatusCode);

        var mailingId = (await (await _bestuur.PostAsJsonAsync("/api/v1/admin/mailing/mailings", MailingBody(listId))).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        // Testmail naar de ingelogde gebruiker.
        Assert.Equal(HttpStatusCode.OK, (await _bestuur.PostAsync($"/api/v1/admin/mailing/mailings/{mailingId}/test", null)).StatusCode);
        Assert.Contains(_api.Emails.Sent, m => m.To == "bestuur@example.com" && m.Subject == "[TEST] Uitnodiging pronkzitting");

        // Versturen: vastgelegd per ontvanger, verdeeld over de tijd (60 per uur = één per minuut).
        var send = await (await _bestuur.PostAsync($"/api/v1/admin/mailing/mailings/{mailingId}/send", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, send.GetProperty("recipients").GetInt32());
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            var planned = await db.Outbox.AsNoTracking().Where(m => m.Type == MailingService.MailMessageType).OrderBy(m => m.LockedUntil).Select(m => m.LockedUntil).ToListAsync();
            Assert.Null(planned[0]);
            Assert.Equal(TimeSpan.FromMinutes(1), planned[2]!.Value - planned[1]!.Value);
        }

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/mailing/mailings/{mailingId}", MailingBody(listId))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PostAsync($"/api/v1/admin/mailing/mailings/{mailingId}/send", null)).StatusCode);

        await RunOutboxAsync();
        var mails = _api.Emails.Sent.Where(m => m.Subject == "Uitnodiging pronkzitting").ToList();
        Assert.Equal(["anna@example.com", "gast@example.com", "piet@example.com"], mails.Select(m => m.To).Order());
        var toGerda = mails.Single(m => m.To == "gast@example.com");
        Assert.Contains("Beste Gerda,", toGerda.Html);
        Assert.Equal(("secretaris@vrolijkedrammers.nl", "secretaris"), (toGerda.ReplyTo, toGerda.From));
        Assert.Contains("Afmelden voor nieuwsbrieven en uitnodigingen: https://dvd.test/afmelden?t=", toGerda.PlainText);

        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/mailing/mailings/{mailingId}");
        Assert.Equal("Sent", detail.GetProperty("status").GetString());
        Assert.Equal(3, detail.GetProperty("progress").GetProperty("sent").GetInt32());

        // Een groep die in een mailing is gebruikt, kan niet weg.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.DeleteAsync($"/api/v1/admin/mailing/lists/{listId}")).StatusCode);

        // Afmelden via de link: eerst een knop, dan afgemeld.
        var link = UnsubscribeLink().Match(toGerda.PlainText).Groups[1].Value;
        var guest = _api.CreateClient();
        var page = await guest.GetStringAsync(link);
        Assert.Contains("g***@example.com", page);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["t"] = Uri.UnescapeDataString(link[(link.IndexOf("t=", StringComparison.Ordinal) + 2)..]),
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups[1].Value,
        });
        var done = await guest.PostAsync("/afmelden", form);
        Assert.Contains("Je bent afgemeld", await done.Content.ReadAsStringAsync());
        Assert.Contains(await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/mailing/unsubscribes") ?? [],
            u => u.GetProperty("email").GetString() == "gast@example.com");
        Assert.Contains("Deze link werkt niet", await guest.GetStringAsync("/afmelden?t=nep"));

        // Een kopie is weer een concept.
        var copy = await _bestuur.PostAsync($"/api/v1/admin/mailing/mailings/{mailingId}/duplicate", null);
        var copyId = (await copy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal("Draft", (await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/mailing/mailings/{copyId}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Standaardgroep_alle_leden_en_alleen_met_het_recht_mailing()
    {
        await MemberAsync("0201", "Karin Lid", "Karin", "karin@example.com");
        var lists = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/mailing/lists");
        var all = Assert.Single(lists!, l => l.GetProperty("allMembers").GetBoolean());
        Assert.Equal("Alle leden", all.GetProperty("name").GetString());
        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/mailing/lists/{all.GetProperty("id").GetGuid()}");
        Assert.Equal(1, detail.GetProperty("audience").GetProperty("recipients").GetInt32());

        var lid = _api.ClientFor((await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/mailing/mailings")).StatusCode);
    }

    [Fact]
    public async Task Informatiebrief_aan_alle_adverteerders_vanaf_de_voorzitter()
    {
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            db.Advertisers.AddRange(
                new Modules.Membership.Advertisers.Advertiser { Id = IdGenerator.NewId(), Number = 1, CompanyName = "Bakkerij De Test", ContactName = "Jan Bakker", Email = "bakker@example.com" },
                new Modules.Membership.Advertisers.Advertiser { Id = IdGenerator.NewId(), Number = 2, CompanyName = "Garage Proef", Email = "garage@example.com" },
                new Modules.Membership.Advertisers.Advertiser { Id = IdGenerator.NewId(), Number = 3, CompanyName = "Zonder Mail" },
                new Modules.Membership.Advertisers.Advertiser { Id = IdGenerator.NewId(), Number = 4, CompanyName = "Gestopt", Email = "weg@example.com", Active = false });
            await db.SaveChangesAsync();
        }

        var lists = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/mailing/lists");
        var advertisers = Assert.Single(lists!, l => l.GetProperty("allAdvertisers").GetBoolean());
        Assert.Equal("Adverteerders", advertisers.GetProperty("name").GetString());
        var listId = advertisers.GetProperty("id").GetGuid();

        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/mailing/mailings", new
        {
            kind = "Newsletter",
            sender = "Chairman",
            subject = "Informatie voor {bedrijf}",
            preheader = (string?)null,
            listIds = new[] { listId },
            blocks = new object[] { new { type = "text", text = "Beste {voornaam},\n\nDank voor de steun van {bedrijf}." } },
        });
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal("Chairman", (await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/mailing/mailings/{id}")).GetProperty("sender").GetString());

        var send = await (await _bestuur.PostAsync($"/api/v1/admin/mailing/mailings/{id}/send", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, send.GetProperty("recipients").GetInt32());
        await RunOutboxAsync();
        var toBakker = _api.Emails.Sent.Single(m => m.To == "bakker@example.com");
        Assert.Equal(("Informatie voor Bakkerij De Test", "voorzitter@vrolijkedrammers.nl", "voorzitter"), (toBakker.Subject, toBakker.ReplyTo, toBakker.From));
        Assert.Contains("Beste Jan,", toBakker.Html);
        Assert.Contains("Dank voor de steun van Bakkerij De Test.", toBakker.PlainText);
        Assert.Contains("voorzitter@vrolijkedrammers.nl | www.vrolijkedrammers.nl", toBakker.PlainText);
        Assert.Contains("Beste Garage Proef,", _api.Emails.Sent.Single(m => m.To == "garage@example.com").Html);
    }

    [Fact]
    public async Task Foto_in_een_mailing_is_openbaar_een_losse_upload_niet()
    {
        var upload = await _bestuur.PostAsync("/api/v1/admin/mailing/images", ContentTests.Multipart("file", "zaal.jpg", TestImages.JpegWithGps()));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var path = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("path").GetString()!;
        var other = (await (await _bestuur.PostAsync("/api/v1/admin/mailing/images", ContentTests.Multipart("file", "los.jpg", TestImages.JpegWithGps())))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("path").GetString()!;

        var lists = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/mailing/lists");
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/mailing/mailings", new
        {
            kind = "Newsletter",
            subject = "Foto's",
            preheader = (string?)null,
            listIds = new[] { lists![0].GetProperty("id").GetGuid() },
            blocks = new object[] { new { type = "image", image = path, text = "De zaal" } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var guest = _api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(MailingService.MediaPath(path))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync(MailingService.MediaPath(other))).StatusCode);
    }

    [GeneratedRegex(@"https://dvd\.test(/afmelden\?t=\S+)")]
    private static partial Regex UnsubscribeLink();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
