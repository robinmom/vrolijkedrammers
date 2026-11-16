using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Worker.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Drammers.IntegrationTests;

/// <summary>
/// Vergaderingen (2026-11-16): een vergadering hangt aan een agenda-item met de juiste doelgroep, de agenda en de
/// notulen zijn bijlagen van dat agenda-item, en de agenda gaat in de huisstijl naar de houders van de rol.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MeetingTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _admin = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> MeetingAsync(string audience, string title)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/admin/meetings", new
        {
            audience,
            eventId = (Guid?)null,
            title,
            startAt = _api.Clock.UtcNow.AddDays(7),
            endAt = _api.Clock.UtcNow.AddDays(7).AddHours(2),
            locationName = "Kantine",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<JsonElement> GetAsync(Guid id) => _admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/meetings/{id}");

    private Task<HttpResponseMessage> AgendaAsync(Guid id, params (string Title, string? Note)[] items) =>
        _admin.PutAsJsonAsync($"/api/v1/admin/meetings/{id}/agenda", new { items = items.Select(i => new { title = i.Title, note = i.Note }) });

    private async Task ProcessOutboxAsync()
    {
        var processor = new OutboxProcessor(_api.Services.GetRequiredService<IServiceScopeFactory>(), new OutboxSignal(), TimeProvider.System,
            NullLogger<OutboxProcessor>.Instance);
        while (await processor.ProcessBatchAsync(default) > 0)
        {
        }
    }

    [Fact]
    public async Task Vergadering_krijgt_een_agendaitem_voor_de_eigen_doelgroep()
    {
        var id = await MeetingAsync("Board", "Bestuursvergadering januari");
        var meeting = await GetAsync(id);
        var eventId = meeting.GetProperty("meeting").GetProperty("eventId").GetGuid();

        // Het agenda-item is alleen voor het bestuur zichtbaar, dus nooit op de website, en staat in een eigen categorie.
        var agendaItem = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/events/{eventId}");
        Assert.Equal("Restricted", agendaItem.GetProperty("publication").GetProperty("visibility").GetString());
        Assert.Equal(["bestuur"], agendaItem.GetProperty("publication").GetProperty("audienceRoles").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal("Published", agendaItem.GetProperty("publication").GetProperty("status").GetString());
        var categories = await _admin.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/events/categories") ?? [];
        Assert.Contains(categories, c => c.GetProperty("id").GetInt32() == agendaItem.GetProperty("categoryId").GetInt32()
            && c.GetProperty("name").GetString() == "Vergadering");

        // Een tweede vergadering bij hetzelfde agenda-item kan niet.
        var twice = await _admin.PostAsJsonAsync("/api/v1/admin/meetings", new { audience = "Board", eventId });
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);

        // Van soort veranderen verhuist ook de doelgroep van het agenda-item.
        var update = await _admin.PutAsJsonAsync($"/api/v1/admin/meetings/{id}", new
        {
            audience = "Both",
            title = "Gezamenlijke vergadering",
            startAt = _api.Clock.UtcNow.AddDays(8),
            endAt = (DateTimeOffset?)null,
            locationName = "De Duif",
            intro = (string?)null,
            sender = "Chairman",
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var moved = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/events/{eventId}");
        Assert.Equal(["bestuur", "raad-van-elf"],
            moved.GetProperty("publication").GetProperty("audienceRoles").EnumerateArray().Select(r => r.GetString()!).Order());
        Assert.Equal("Gezamenlijke vergadering", moved.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Agenda_en_notulen_gaan_in_de_huisstijl_naar_de_rolhouders()
    {
        // Een bestuurslid zonder e-mailadres hoort erbij, maar krijgt geen mail; dat moet het portal melden.
        await _api.CreateUserAsync(string.Empty, DefaultRoles.Bestuur);
        await _api.CreateUserAsync("raadslid@example.com", DefaultRoles.RaadVanElf);
        var id = await MeetingAsync("Board", "Bestuursvergadering");

        Assert.Equal(HttpStatusCode.NoContent, (await AgendaAsync(id, ("Opening", null), ("Begroting 2027", "ter besluitvorming"))).StatusCode);

        // De notulen: een bijlage van het agenda-item, dus ook te lezen in de app.
        var upload = await _admin.PostAsync($"/api/v1/admin/meetings/{id}/documents",
            ContentTests.Multipart("file", "notulen.pdf", Encoding.ASCII.GetBytes("%PDF-1.4 notulen"), "application/pdf"));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var documentId = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var meeting = await GetAsync(id);
        var audience = meeting.GetProperty("audience");
        Assert.Equal(["bestuur@example.com"], audience.GetProperty("recipients").EnumerateArray().Select(r => r.GetProperty("email").GetString()));
        Assert.Single(audience.GetProperty("withoutEmail").EnumerateArray());
        Assert.Equal("notulen.pdf", meeting.GetProperty("documents")[0].GetProperty("fileName").GetString());

        var send = await _admin.PostAsJsonAsync($"/api/v1/admin/meetings/{id}/agenda/send", new { documentIds = new[] { documentId }, extraEmails = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        Assert.Equal(1, (await send.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recipients").GetInt32());

        await ProcessOutboxAsync();
        var mail = Assert.Single(_api.Emails.Sent);
        Assert.Equal("bestuur@example.com", mail.To);
        Assert.StartsWith("Agenda Bestuursvergadering", mail.Subject, StringComparison.Ordinal);
        // Via de mailbox secretaris@, dus buiten de ACS-limiet om, met de notulen als bijlage.
        Assert.True(mail.ViaMailbox);
        Assert.Equal("secretaris", mail.From);
        var attachment = Assert.Single(mail.Attachments!);
        Assert.Equal(("notulen.pdf", "application/pdf"), (attachment.Name, attachment.ContentType));
        // De agenda staat genummerd in de mail, met de toelichting erbij.
        Assert.Contains("Opening", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Begroting 2027", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("ter besluitvorming", mail.PlainText, StringComparison.Ordinal);
        Assert.Contains("notulen.pdf", mail.PlainText, StringComparison.Ordinal);

        var sent = await GetAsync(id);
        Assert.NotNull(sent.GetProperty("meeting").GetProperty("agendaSentAt").GetString());
        Assert.Equal(1, sent.GetProperty("meeting").GetProperty("agendaRecipientCount").GetInt32());

        var audit = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-log?entityType=Meeting");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "meeting.agenda-sent");
    }

    [Fact]
    public async Task Gezamenlijke_vergadering_bereikt_bestuur_en_raad_van_elf()
    {
        await _api.CreateUserAsync("raadslid@example.com", DefaultRoles.RaadVanElf);
        var board = await MeetingAsync("Board", "Bestuursvergadering");
        var both = await MeetingAsync("Both", "Gezamenlijke vergadering");

        Assert.Equal(["bestuur@example.com"],
            (await GetAsync(board)).GetProperty("audience").GetProperty("recipients").EnumerateArray().Select(r => r.GetProperty("email").GetString()));
        Assert.Equal(["bestuur@example.com", "raadslid@example.com"],
            (await GetAsync(both)).GetProperty("audience").GetProperty("recipients").EnumerateArray().Select(r => r.GetProperty("email").GetString()!).Order());

        // Zonder agendapunten gaat er niets de deur uit.
        var tooEarly = await _admin.PostAsJsonAsync($"/api/v1/admin/meetings/{both}/agenda/send", new { documentIds = Array.Empty<Guid>(), extraEmails = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooEarly.StatusCode);
        Assert.Empty(_api.Emails.Sent);
    }

    [Fact]
    public async Task Agenda_overnemen_van_de_vorige_vergadering_van_dezelfde_soort()
    {
        var first = await MeetingAsync("Board", "Bestuursvergadering december");
        await AgendaAsync(first, ("Opening", null), ("Notulen", "ter vaststelling"), ("Sluiting", null));

        // De tweede vergadering is later, dus die neemt de agenda van de eerste over.
        var response = await _admin.PostAsJsonAsync("/api/v1/admin/meetings", new
        {
            audience = "Board",
            eventId = (Guid?)null,
            title = "Bestuursvergadering januari",
            startAt = _api.Clock.UtcNow.AddDays(30),
            endAt = (DateTimeOffset?)null,
            locationName = (string?)null,
        });
        var second = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var copied = await _admin.PostAsync($"/api/v1/admin/meetings/{second}/agenda/copy-previous", null);
        Assert.Equal(HttpStatusCode.OK, copied.StatusCode);
        Assert.Equal(3, (await copied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").GetInt32());
        var items = (await GetAsync(second)).GetProperty("meeting").GetProperty("agendaItems");
        Assert.Equal(["Opening", "Notulen", "Sluiting"], items.EnumerateArray().Select(a => a.GetProperty("title").GetString()));
        Assert.Equal("ter vaststelling", items[1].GetProperty("note").GetString());

        // Van de eerste vergadering is er niets eerder, dus daar valt niets over te nemen.
        var nothing = await _admin.PostAsync($"/api/v1/admin/meetings/{first}/agenda/copy-previous", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nothing.StatusCode);
    }

    [Fact]
    public async Task Vergadering_verwijderen_laat_het_agendaitem_staan()
    {
        var id = await MeetingAsync("CouncilOfEleven", "Vergadering Raad van Elf");
        var eventId = (await GetAsync(id)).GetProperty("meeting").GetProperty("eventId").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/admin/meetings/{id}")).StatusCode);
        Assert.Empty(await _admin.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/meetings") ?? []);
        var agendaItem = await _admin.GetAsync($"/api/v1/admin/events/{eventId}");
        Assert.Equal(HttpStatusCode.OK, agendaItem.StatusCode);
    }
}
