using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Parade.Registrations;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 11a: optochtinschrijving door leden, opgavenummer (ADR-011), statusbeleid, documenten.</summary>
[Collection(SqlServerCollection.Name)]
public class ParadeRegistrationTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private Guid _paradeId;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        var now = _api.Clock.UtcNow;
        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/parades", new
        {
            carnivalYearId = 1,
            name = "Optocht Loil 2027",
            paradeDate = "2027-02-07",
            startTime = "13:30:00",
            startLocation = "Dorpsplein Loil",
            routeDescription = "Rondje door het dorp",
            routeLengthKm = 3.2m,
            registrationOpensAt = now.AddDays(-1),
            registrationClosesAt = now.AddDays(20),
            editDeadlineAt = (DateTimeOffset?)null,
            subjectRequired = true,
            defaultSpacingMeters = 5m,
            maxDocumentsPerRegistration = 2,
            maxDocumentSizeMb = 5,
            status = "RegistrationOpen",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        _paradeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    /// <summary>Groepsverantwoordelijke (per gebruiker aangevinkt; alleen die mag inschrijven).</summary>
    private async Task<(Guid UserId, HttpClient Client)> LidAsync(string email)
    {
        var (userId, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid, DefaultRoles.Groepsverantwoordelijke);
        return (userId, _api.ClientFor(oid));
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Complete(string version, int category = 3, int adults = 12, int children = 0, bool jurySame = true, object? jury = null, string group = "De Knotwilgen") => new
    {
        version,
        groupName = group,
        contactName = "Piet Lid",
        contactPhone = "06 12345678",
        contactEmail = "piet@example.com",
        categoryId = category,
        subject = "Wilde westen",
        subjectDescription = (string?)null,
        childrenCount = children,
        adultCount = adults,
        buildAddress = new { street = "Dorpsstraat", houseNumber = "1", addition = (string?)null, postalCode = "6999aa", city = "Loil", country = "NL" },
        juryInspectionSameAsBuildAddress = jurySame,
        juryInspectionAddress = jury,
        estimatedLengthMeters = 12.5m,
        additionalInformation = "Met muziek",
    };

    private async Task<JsonElement> DraftAsync(HttpClient client) => await JsonAsync(await client.PostAsync("/api/v1/parade/registrations", null), HttpStatusCode.Created);

    private async Task<JsonElement> SaveAsync(HttpClient client, JsonElement registration, object body, HttpStatusCode expected = HttpStatusCode.OK) =>
        await JsonAsync(await client.PutAsJsonAsync($"/api/v1/parade/registrations/{registration.GetProperty("id").GetGuid()}", body), expected);

    private async Task RunOutboxAsync()
    {
        string[] types = [ParadeRegistrations.SubmittedMailMessageType, ParadeReview.StatusMailMessageType];
        var messages = await WithDbAsync(db => db.Outbox.AsNoTracking().Where(m => m.ProcessedAt == null && types.Contains(m.Type)).ToListAsync());
        foreach (var message in messages)
        {
            using var scope = _api.Services.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IOutboxMessageHandler>().Single(h => h.Type == message.Type);
            await handler.HandleAsync(new OutboxEnvelope(message.Id, message.Type, message.Payload, 0), CancellationToken.None);
            await WithDbAsync(db => db.Outbox.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAt, DateTime.UtcNow)));
        }
    }

    [Fact]
    public async Task Lid_slaat_concept_op_gaat_later_verder_en_dient_in_met_opgavenummer()
    {
        var (userId, lid) = await LidAsync("piet@example.com");
        var info = await lid.GetFromJsonAsync<JsonElement>("/api/v1/parade/current");
        Assert.True(info.GetProperty("registrationOpen").GetBoolean());
        Assert.Equal(9, (await lid.GetFromJsonAsync<JsonElement>("/api/v1/parade/categories")).GetArrayLength());

        var draft = await DraftAsync(lid);
        Assert.Equal(("Draft", "piet@example.com"), (draft.GetProperty("status").GetString(), draft.GetProperty("contactEmail").GetString()));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("registrationNumber").ValueKind);

        // Halverwege opslaan: een concept mag onvolledig zijn.
        var partial = await SaveAsync(lid, draft, new { version = draft.GetProperty("version").GetString(), groupName = "De Knotwilgen", childrenCount = 0, adultCount = 0, juryInspectionSameAsBuildAddress = true });
        Assert.Equal("Draft", partial.GetProperty("status").GetString());

        // Later verder, alles invullen en indienen.
        var reloaded = await lid.GetFromJsonAsync<JsonElement>($"/api/v1/parade/registrations/{draft.GetProperty("id").GetGuid()}");
        var saved = await SaveAsync(lid, reloaded, Complete(reloaded.GetProperty("version").GetString()!));
        Assert.Equal(("+31612345678", "06 12345678", "6999 AA"), (saved.GetProperty("contactPhone").GetString(),
            saved.GetProperty("contactPhoneDisplay").GetString(), saved.GetProperty("buildAddress").GetProperty("postalCode").GetString()));
        var validation = await lid.PostAsync($"/api/v1/parade/registrations/{draft.GetProperty("id").GetGuid()}/validate", null);
        Assert.Empty(await validation.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);

        var submitted = await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{draft.GetProperty("id").GetGuid()}/submit", null));
        Assert.Equal(("Submitted", 1), (submitted.GetProperty("status").GetString(), submitted.GetProperty("registrationNumber").GetInt32()));
        var again = await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{draft.GetProperty("id").GetGuid()}/submit", null));
        Assert.Equal(1, again.GetProperty("registrationNumber").GetInt32());

        await RunOutboxAsync();
        var mail = Assert.Single(_api.Emails.Sent, m => m.To == "piet@example.com");
        Assert.Contains("opgavenummer 1", mail.Subject);
        Assert.Contains("niet jullie startnummer", mail.PlainText);
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title.Contains("nr. 1"))));
        Assert.Equal("6999 AA", (await WithDbAsync(db => db.ParadeBuildLocations.SingleAsync(l => l.UserId == userId))).Address.PostalCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lid.DeleteAsync($"/api/v1/parade/registrations/{draft.GetProperty("id").GetGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Vijftig_gelijktijdige_inzendingen_krijgen_1_tot_50_zonder_gaten()
    {
        var clients = new List<(HttpClient Client, Guid Id)>();
        for (var i = 0; i < 50; i++)
        {
            var (_, lid) = await LidAsync($"lid{i}@example.com");
            var draft = await DraftAsync(lid);
            await SaveAsync(lid, draft, Complete(draft.GetProperty("version").GetString()!, group: $"Groep {i}"));
            clients.Add((lid, draft.GetProperty("id").GetGuid()));
        }

        var responses = await Task.WhenAll(clients.Select(c => c.Client.PostAsync($"/api/v1/parade/registrations/{c.Id}/submit", null)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var numbers = await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.RegistrationNumber != null).Select(r => r.RegistrationNumber!.Value).OrderBy(n => n).ToListAsync());
        Assert.Equal(Enumerable.Range(1, 50), numbers);
        Assert.Equal(50, await WithDbAsync(db => db.ParadeNumberSequences.Where(s => s.ParadeId == _paradeId).Select(s => s.LastRegistrationNumber).SingleAsync()));
    }

    [Theory]
    [InlineData(3, 8, "Deze categorie vereist minimaal 10 volwassenen (nu 8).")]
    [InlineData(5, 3, "Deze categorie staat maximaal 2 volwassenen toe (nu 3).")]
    public async Task Deelnemers_buiten_de_categorie_worden_geblokkeerd(int category, int adults, string message)
    {
        var (_, lid) = await LidAsync("piet@example.com");
        var draft = await DraftAsync(lid);
        var saved = await SaveAsync(lid, draft, Complete(draft.GetProperty("version").GetString()!, category, adults));
        Assert.Contains(message, saved.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("message").GetString()));

        var submit = await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{draft.GetProperty("id").GetGuid()}/submit", null), HttpStatusCode.UnprocessableEntity);
        var issue = Assert.Single(submit.GetProperty("issues").EnumerateArray());
        Assert.Equal(("adultCount", message), (issue.GetProperty("field").GetString(), issue.GetProperty("message").GetString()));
        Assert.Null(await WithDbAsync(db => db.ParadeRegistrations.Select(r => r.RegistrationNumber).SingleAsync()));
    }

    [Fact]
    public async Task Stalling_jury_gelijk_aan_bouwadres_vraagt_geen_tweede_adres()
    {
        var (_, lid) = await LidAsync("piet@example.com");
        var draft = await DraftAsync(lid);
        var id = draft.GetProperty("id").GetGuid();
        var different = await SaveAsync(lid, draft, Complete(draft.GetProperty("version").GetString()!, jurySame: false));
        var submit = await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null), HttpStatusCode.UnprocessableEntity);
        Assert.Equal("juryInspection", Assert.Single(submit.GetProperty("issues").EnumerateArray()).GetProperty("field").GetString());

        var same = await SaveAsync(lid, different, Complete(different.GetProperty("version").GetString()!, jurySame: true));
        Assert.Equal("Dorpsstraat", same.GetProperty("buildAddress").GetProperty("street").GetString());
        await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null));
    }

    [Fact]
    public async Task Na_indienen_alleen_toegestane_velden_en_intrekken_houdt_het_opgavenummer()
    {
        var (_, lid) = await LidAsync("piet@example.com");
        var draft = await DraftAsync(lid);
        var id = draft.GetProperty("id").GetGuid();
        await SaveAsync(lid, draft, Complete(draft.GetProperty("version").GetString()!));
        var submitted = await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null));
        Assert.Contains("contactPhone", submitted.GetProperty("editableFields").EnumerateArray().Select(f => f.GetString()));
        Assert.DoesNotContain("groupName", submitted.GetProperty("editableFields").EnumerateArray().Select(f => f.GetString()));

        var rename = await lid.PutAsJsonAsync($"/api/v1/parade/registrations/{id}", Complete(submitted.GetProperty("version").GetString()!, group: "Andere naam"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rename.StatusCode);
        Assert.Contains("Groepsnaam", (await rename.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());

        var body = JsonSerializer.SerializeToNode(Complete(submitted.GetProperty("version").GetString()!))!;
        body["contactPhone"] = "0612345679";
        var phone = await JsonAsync(await lid.PutAsJsonAsync($"/api/v1/parade/registrations/{id}", body));
        Assert.Equal("+31612345679", phone.GetProperty("contactPhone").GetString());
        Assert.True(await WithDbAsync(db => db.ParadeRegistrationHistory.AnyAsync(h => h.RegistrationId == id && h.FieldName == "ContactPhone"
            && h.OldValue == "+31612345678" && h.NewValue == "+31612345679" && h.ChangeSource == RegistrationSource.App)));

        // Verouderde versie: iemand anders heeft intussen opgeslagen.
        var stale = await lid.PutAsJsonAsync($"/api/v1/parade/registrations/{id}", Complete(submitted.GetProperty("version").GetString()!));
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        var withdrawn = await JsonAsync(await lid.PostAsJsonAsync($"/api/v1/parade/registrations/{id}/withdraw", new { reason = "Groep gaat niet door" }));
        Assert.Equal(("Withdrawn", 1), (withdrawn.GetProperty("status").GetString(), withdrawn.GetProperty("registrationNumber").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict, (await lid.PostAsJsonAsync($"/api/v1/parade/registrations/{id}/withdraw", new { reason = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task Alleen_beheerders_zien_de_inschrijving_en_een_mede_beheerder_kan_meedoen()
    {
        var (_, eigenaar) = await LidAsync("piet@example.com");
        var (_, ander) = await LidAsync("anna@example.com");
        var draft = await DraftAsync(eigenaar);
        var id = draft.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await ander.GetAsync($"/api/v1/parade/registrations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ander.PutAsJsonAsync($"/api/v1/parade/registrations/{id}", Complete(draft.GetProperty("version").GetString()!))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await eigenaar.PostAsJsonAsync($"/api/v1/parade/registrations/{id}/managers", new { email = "anna@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ander.GetAsync($"/api/v1/parade/registrations/{id}")).StatusCode);
        Assert.Equal(2, (await eigenaar.GetFromJsonAsync<JsonElement>($"/api/v1/parade/registrations/{id}/managers")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await ander.PostAsJsonAsync($"/api/v1/parade/registrations/{id}/managers", new { email = "piet@example.com" })).StatusCode);
    }

    [Fact]
    public async Task Indienen_buiten_de_inschrijfperiode_geeft_409()
    {
        var (_, lid) = await LidAsync("piet@example.com");
        var draft = await DraftAsync(lid);
        await SaveAsync(lid, draft, Complete(draft.GetProperty("version").GetString()!));
        _api.Clock.Advance(TimeSpan.FromDays(30));

        var submit = await lid.PostAsync($"/api/v1/parade/registrations/{draft.GetProperty("id").GetGuid()}/submit", null);

        Assert.Equal(HttpStatusCode.Conflict, submit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lid.PostAsync("/api/v1/parade/registrations", null)).StatusCode);
    }

    [Fact]
    public async Task Documenten_uploaden_met_limiet_en_een_korte_downloadlink()
    {
        var (_, lid) = await LidAsync("piet@example.com");
        var id = (await DraftAsync(lid)).GetProperty("id").GetGuid();
        async Task<HttpResponseMessage> UploadAsync(string name)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", name);
            form.Add(new StringContent("Insurance"), "type");
            return await lid.PostAsync($"/api/v1/parade/registrations/{id}/documents", form);
        }

        var first = await JsonAsync(await UploadAsync("verzekering <polis>.pdf"), HttpStatusCode.Created);
        Assert.Equal(("verzekering polis.pdf", "Insurance"), (first.GetProperty("fileName").GetString(), first.GetProperty("documentType").GetString()));
        await JsonAsync(await UploadAsync("tekening.pdf"), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await UploadAsync("derde.pdf")).StatusCode);

        var link = await lid.GetFromJsonAsync<JsonElement>($"/api/v1/parade/registrations/{id}/documents/{first.GetProperty("id").GetGuid()}/download");
        Assert.Contains("parade-documents", link.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Herinnering_voor_concepten_vlak_voor_de_sluiting_een_keer()
    {
        var (_, lid) = await LidAsync("piet@example.com");
        await DraftAsync(lid);
        _api.Clock.Advance(TimeSpan.FromDays(18));

        using var scope = _api.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ParadeDeadlineReminderJob>();
        Assert.Equal(1, await job.RunAsync(default));
        Assert.Equal(0, await job.RunAsync(default));
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title == "Inschrijving optocht sluit bijna")));
    }

    [Fact]
    public async Task Beheer_categorieen_en_maar_een_optocht_per_carnavalsjaar()
    {
        var categories = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-categories");
        var small = categories.EnumerateArray().Single(c => c.GetProperty("code").GetString() == "ADULT_WALK_S");
        Assert.Equal((3, 9, "AdultsOnly"), (small.GetProperty("minimumParticipants").GetInt32(), small.GetProperty("maximumParticipants").GetInt32(),
            small.GetProperty("participantCountBasis").GetString()));

        var update = await _bestuur.PutAsJsonAsync($"/api/v1/admin/parade-categories/{small.GetProperty("id").GetInt32()}", new
        {
            code = "ADULT_WALK_S",
            name = "Volwassenen Loopgroepen klein (3-9)",
            ageGroup = "Adult",
            type = "WalkingGroupSmall",
            minimumParticipants = 3,
            maximumParticipants = 9,
            participantCountBasis = "AdultsOnly",
            validationMode = "Block",
            hasVehicle = false,
            active = false,
            sortOrder = 40,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(8, (await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/parade/categories")).GetArrayLength());

        var duplicate = await _bestuur.PostAsJsonAsync("/api/v1/admin/parades", new
        {
            carnivalYearId = 1,
            name = "Tweede optocht",
            paradeDate = "2027-02-08",
            startTime = "13:30:00",
            startLocation = (string?)null,
            routeDescription = (string?)null,
            routeLengthKm = (decimal?)null,
            registrationOpensAt = _api.Clock.UtcNow,
            registrationClosesAt = _api.Clock.UtcNow.AddDays(1),
            editDeadlineAt = (DateTimeOffset?)null,
            subjectRequired = true,
            defaultSpacingMeters = 5m,
            maxDocumentsPerRegistration = 5,
            maxDocumentSizeMb = 10,
            status = "Planned",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var (_, lid) = await LidAsync("piet@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/parades")).StatusCode);
    }

    [Fact]
    public async Task Een_gewoon_lid_zonder_de_rol_mag_niet_inschrijven_en_ziet_de_informatie()
    {
        var (_, oid) = await _api.CreateUserAsync("gewoon@example.com", DefaultRoles.Lid);
        var gewoon = _api.ClientFor(oid);
        Assert.Equal(HttpStatusCode.Forbidden, (await gewoon.PostAsync("/api/v1/parade/registrations", null)).StatusCode);

        var parade = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/parades/{_paradeId}");
        var body = JsonSerializer.SerializeToNode(parade)!;
        body["infoText"] = "## Meedoen?\nVraag het bestuur om je als **groepsverantwoordelijke** aan te melden.";
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/parades/{_paradeId}", body)).StatusCode);
        var info = await gewoon.GetFromJsonAsync<JsonElement>("/api/v1/parade/current");
        Assert.Contains("<strong>groepsverantwoordelijke</strong>", info.GetProperty("infoHtml").GetString());
    }

    [Fact]
    public async Task Concept_is_vooringevuld_met_de_groepsnaam_uit_e_Boekhouden_en_de_laatste_bouwlocatie()
    {
        var (userId, lid) = await LidAsync("piet@example.com");
        await WithDbAsync(async db =>
        {
            var member = new Drammers.Modules.Membership.Members.Member
            {
                Id = Drammers.SharedKernel.Identifiers.IdGenerator.NewId(),
                MemberNumber = "777",
                FullName = "Piet Lid",
                ParadeGroupName = "De Knotwilgen",
                MembershipStatus = Drammers.Modules.Membership.Members.MembershipStatus.Active,
            };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = member.Id;
            db.ParadeBuildLocations.Add(new ParadeBuildLocation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                Address = new Address { Street = "Schuurweg", HouseNumber = "4", PostalCode = "6999 AB", City = "Loil" },
            });
            return await db.SaveChangesAsync();
        });

        var draft = await DraftAsync(lid);

        Assert.Equal(("De Knotwilgen", "Schuurweg"), (draft.GetProperty("groupName").GetString(), draft.GetProperty("buildAddress").GetProperty("street").GetString()));
        var locations = await lid.GetFromJsonAsync<JsonElement>("/api/v1/parade/build-locations");
        var location = Assert.Single(locations.EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await lid.DeleteAsync($"/api/v1/parade/build-locations/{location.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Empty((await lid.GetFromJsonAsync<JsonElement>("/api/v1/parade/build-locations")).EnumerateArray());
    }

    [Fact]
    public async Task Commissie_neemt_in_behandeling_vraagt_aanvulling_en_keurt_goed_de_groep_hoort_het()
    {
        var (_, lid) = await LidAsync("piet@example.com");
        var draft = await DraftAsync(lid);
        var id = draft.GetProperty("id").GetGuid();
        await SaveAsync(lid, draft, Complete(draft.GetProperty("version").GetString()!));
        await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null));
        var (_, commissieOid) = await _api.CreateUserAsync("commissie@example.com", DefaultRoles.Lid, DefaultRoles.Optochtcommissie);
        var commissie = _api.ClientFor(commissieOid);

        var list = await commissie.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-registrations?status=Submitted");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        async Task<HttpResponseMessage> ActAsync(string action, string? reason = null) =>
            await commissie.PostAsJsonAsync($"/api/v1/admin/parade-registrations/{id}/review", new { action, reason });

        Assert.Equal(HttpStatusCode.NoContent, (await ActAsync("StartReview")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ActAsync("RequestInformation")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ActAsync("RequestInformation", "Stuur een tekening van de wagen mee.")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ActAsync("StartReview")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ActAsync("Approve")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ActAsync("Approve")).StatusCode);

        var detail = await commissie.GetFromJsonAsync<JsonElement>($"/api/v1/admin/parade-registrations/{id}");
        Assert.Equal("Approved", detail.GetProperty("status").GetString());
        Assert.Equal(["Submitted", "UnderReview", "AdditionalInformationRequired", "UnderReview", "Approved"],
            detail.GetProperty("statusHistory").EnumerateArray().Skip(1).Select(h => h.GetProperty("toStatus").GetString()));
        Assert.Equal("Approved", (await lid.GetFromJsonAsync<JsonElement>($"/api/v1/parade/registrations/{id}")).GetProperty("status").GetString());

        await RunOutboxAsync();
        Assert.Contains(_api.Emails.Sent, m => m.Subject.Contains("goedgekeurd") && m.PlainText.Contains("definitief"));
        Assert.Contains(_api.Emails.Sent, m => m.Subject.StartsWith("Aanvulling nodig") && m.PlainText.Contains("tekening van de wagen"));
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title == "Inschrijving optocht goedgekeurd")));

        var (_, lidOid) = await _api.CreateUserAsync("gewoon@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lidOid).GetAsync("/api/v1/admin/parade-registrations")).StatusCode);
    }

    [Fact]
    public async Task Groep_vult_gevraagde_aanvulling_aan_en_dient_opnieuw_in_de_commissie_ziet_het()
    {
        var (_, lid) = await LidAsync("piet@example.com");
        var draft = await DraftAsync(lid);
        var id = draft.GetProperty("id").GetGuid();
        await SaveAsync(lid, draft, Complete(draft.GetProperty("version").GetString()!));
        await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null));
        var (_, commissieOid) = await _api.CreateUserAsync("commissie@example.com", DefaultRoles.Lid, DefaultRoles.Optochtcommissie);
        var commissie = _api.ClientFor(commissieOid);
        Assert.Equal(HttpStatusCode.NoContent, (await commissie.PostAsJsonAsync($"/api/v1/admin/parade-registrations/{id}/review",
            new { action = "RequestInformation", reason = "Kies een andere groepsnaam." })).StatusCode);

        var asked = await lid.GetFromJsonAsync<JsonElement>($"/api/v1/parade/registrations/{id}");
        Assert.Equal("Kies een andere groepsnaam.", asked.GetProperty("reviewReason").GetString());
        Assert.Contains("groupName", asked.GetProperty("editableFields").EnumerateArray().Select(f => f.GetString()));
        var saved = await SaveAsync(lid, asked, Complete(asked.GetProperty("version").GetString()!, group: "De Knotwilgen 2027"));
        Assert.Equal("De Knotwilgen 2027", saved.GetProperty("groupName").GetString());

        var resubmitted = await JsonAsync(await lid.PostAsync($"/api/v1/parade/registrations/{id}/submit", null));
        Assert.Equal("UnderReview", resubmitted.GetProperty("status").GetString());
        Assert.Equal(1, resubmitted.GetProperty("registrationNumber").GetInt32());

        var item = Assert.Single((await commissie.GetFromJsonAsync<JsonElement>("/api/v1/admin/parade-registrations?status=UnderReview")).GetProperty("items").EnumerateArray());
        Assert.True(item.GetProperty("supplementReceived").GetBoolean());
        var last = (await commissie.GetFromJsonAsync<JsonElement>($"/api/v1/admin/parade-registrations/{id}")).GetProperty("statusHistory").EnumerateArray().Last();
        Assert.Equal(("AdditionalInformationRequired", "UnderReview", ParadeReview.SupplementReason),
            (last.GetProperty("fromStatus").GetString(), last.GetProperty("toStatus").GetString(), last.GetProperty("reason").GetString()));
        Assert.True(await WithDbAsync(db => db.Notifications.AnyAsync(n => n.Title == "Aanvulling optocht ontvangen")));
    }

    [Fact]
    public async Task Gast_schrijft_in_zonder_account_pas_na_de_e_mailcode_een_opgavenummer_en_een_statuslink()
    {
        var guest = _api.CreateClient();
        var body = JsonSerializer.SerializeToNode(Complete("", group: "Buurtvereniging Oost"))!;
        body["contactEmail"] = "gast@example.com";
        var missing = await guest.PostAsJsonAsync("/api/v1/parade/public-registrations", new { registration = body, rulesAccepted = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);

        var started = await JsonAsync(await guest.PostAsJsonAsync("/api/v1/parade/public-registrations", new { registration = body, rulesAccepted = true }), HttpStatusCode.Created);
        var id = started.GetProperty("id").GetGuid();
        Assert.Null(await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.Id == id).Select(r => r.RegistrationNumber).SingleAsync()));
        var code = System.Text.RegularExpressions.Regex.Match(_api.Emails.Sent.Last(m => m.To == "gast@example.com").PlainText, @"\b\d{6}\b").Value;

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await guest.PostAsJsonAsync($"/api/v1/parade/public-registrations/{id}/verify-email", new { code = code == "000000" ? "111111" : "000000" })).StatusCode);
        var verified = await JsonAsync(await guest.PostAsJsonAsync($"/api/v1/parade/public-registrations/{id}/verify-email", new { code }));
        Assert.Equal(1, verified.GetProperty("registrationNumber").GetInt32());
        var token = verified.GetProperty("statusToken").GetString();
        Assert.Contains(_api.Emails.Sent, m => m.To == "gast@example.com" && m.PlainText.Contains($"?status={token}"));

        var status = await guest.GetFromJsonAsync<JsonElement>($"/api/v1/parade/public-registrations/status?token={token}");
        Assert.Equal(("Buurtvereniging Oost", 1, "Submitted"), (status.GetProperty("groupName").GetString(), status.GetProperty("registrationNumber").GetInt32(), status.GetProperty("status").GetString()));
        Assert.False(status.TryGetProperty("contactEmail", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("/api/v1/parade/public-registrations/status?token=onzin")).StatusCode);
        Assert.Equal(RegistrationSource.WebForm, await WithDbAsync(db => db.ParadeRegistrations.Where(r => r.Id == id).Select(r => r.Source).SingleAsync()));
    }

    [Fact]
    public async Task Mede_beheerder_moet_zelf_groepsverantwoordelijke_zijn()
    {
        var (_, eigenaar) = await LidAsync("piet@example.com");
        await _api.CreateUserAsync("gewoon@example.com", DefaultRoles.Lid);
        var id = (await DraftAsync(eigenaar)).GetProperty("id").GetGuid();

        var response = await eigenaar.PostAsJsonAsync($"/api/v1/parade/registrations/{id}/managers", new { email = "gewoon@example.com" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
