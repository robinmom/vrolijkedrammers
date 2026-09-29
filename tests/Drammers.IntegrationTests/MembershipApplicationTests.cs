using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Drammers.Infrastructure.EBoekhouden;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 9b: lid worden (ADR-014 §1): e-mailcode, beoordeling, provisioning met e-Boekhouden, ouders.</summary>
[Collection(SqlServerCollection.Name)]
public partial class MembershipApplicationTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string ValidIban = "NL91ABNA0417164300";
    private readonly FakeEBoekhouden _eb = new();
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private HttpClient _anonymous = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), configure: services => services.AddSingleton<IEBoekhoudenClient>(_eb));
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        _anonymous = _api.CreateClient();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    private DateOnly Today => DateOnly.FromDateTime(_api.Clock.UtcNow.UtcDateTime);

    private object Form(
        string email, int age, string firstName = "Piet", string? guardian = null, string iban = ValidIban, bool mandate = true, string membershipType = "Individual") => new
        {
            firstName,
            namePrefix = "van der",
            lastName = "Berg",
            gender = "m",
            birthDate = Today.AddYears(-age).AddDays(-10),
            addressLine = "Dorpsstraat 1",
            postalCode = "6999 aa",
            city = "Loil",
            email,
            phone = "0612345678",
            guardianName = guardian,
            guardianPhone = guardian is null ? null : "0698765432",
            iban,
            accountHolder = guardian ?? "P. van der Berg",
            mandateConsent = mandate,
            privacyConsent = true,
            photoConsent = false,
            source = "App",
            membershipType,
        };

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();

    private string LastCodeFor(string email) => CodePattern().Match(_api.Emails.Sent.Last(m => m.To == email && m.Subject.Contains("code", StringComparison.Ordinal)).PlainText).Value;

    /// <summary>Formulier + e-mailcode: de aanmelding staat daarna in de wachtrij. Geeft het ID.</summary>
    private async Task<Guid> SubmitAsync(object form, string email)
    {
        var start = await _anonymous.PostAsJsonAsync("/api/v1/membership-applications", form);
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var id = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var verify = await _anonymous.PostAsJsonAsync($"/api/v1/membership-applications/{id}/verify-email", new { code = LastCodeFor(email) });
        Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
        return id;
    }

    private async Task RunProvisioningAsync()
    {
        var messages = await WithDbAsync(db => db.Outbox.AsNoTracking().Where(m => m.Type == MembershipApplications.ProvisionMessageType).OrderBy(m => m.CreatedAt).ToListAsync());
        await WithDbAsync(db => db.Outbox.Where(m => m.Type == MembershipApplications.ProvisionMessageType).ExecuteDeleteAsync());
        foreach (var message in messages)
        {
            using var scope = _api.Services.CreateScope();
            var id = JsonSerializer.Deserialize<MembershipApplications.ProvisionMessage>(message.Payload, JsonSerializerOptions.Web)!.ApplicationId;
            try
            {
                await scope.ServiceProvider.GetRequiredService<MembershipApplications>().RunProvisioningAsync(id, CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                // Mislukte stap: zichtbaar in de aanmelding; opnieuw proberen via het portal.
            }
        }
    }

    [Fact]
    public async Task Volwassene_meldt_zich_aan_bestuur_keurt_goed_en_het_lid_kan_inloggen()
    {
        var id = await SubmitAsync(Form("piet@example.com", 30), "piet@example.com");

        var queue = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/membership-applications?status=Submitted");
        var item = Assert.Single(queue.GetProperty("items").EnumerateArray());
        Assert.Equal("Piet van der Berg", item.GetProperty("fullName").GetString());
        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/membership-applications/{id}");
        Assert.Equal("NL** **** **** 4300", detail.GetProperty("ibanMasked").GetString());
        Assert.Equal("6999 AA", detail.GetProperty("postalCode").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/membership-applications/{id}/start-review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/membership-applications/{id}/approve", null)).StatusCode);
        await RunProvisioningAsync();

        var application = await WithDbAsync(db => db.MembershipApplications.AsNoTracking().SingleAsync(a => a.Id == id));
        Assert.Equal(ApplicationStatus.Activated, application.Status);
        Assert.Null(application.Iban);
        Assert.Null(application.AccountHolder);
        var member = await WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.Id == application.ResultingMemberId));
        // In Dev (schrijven naar e-Boekhouden uit) een gesimuleerd lidnummer.
        Assert.StartsWith("SIM", member.MemberNumber, StringComparison.Ordinal);
        Assert.Empty(_eb.Created);
        Assert.Contains(_api.Emails.Sent, m => m.To == "piet@example.com" && m.Subject.StartsWith("Je account", StringComparison.Ordinal));

        var client = _api.ClientFor(_api.Entra.SignUp("piet@example.com"));
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/member");
        Assert.Equal(member.MemberNumber, me.GetProperty("memberNumber").GetString());
        Assert.True(await WithDbAsync(db => db.AuditLog.AnyAsync(a => a.Action == "membership-application.activated")));
    }

    [Fact]
    public async Task Minderjarige_ouder_krijgt_het_account_met_rol_Ouder_en_een_relatie_ook_voor_een_tweede_kind()
    {
        var first = await SubmitAsync(Form("ouder@example.com", 8, "Sanne", guardian: "Anja van der Berg"), "ouder@example.com");
        var second = await SubmitAsync(Form("ouder@example.com", 11, "Tim", guardian: "Anja van der Berg"), "ouder@example.com");
        foreach (var id in new[] { first, second })
        {
            await _bestuur.PostAsync($"/api/v1/admin/membership-applications/{id}/approve", null);
        }

        await RunProvisioningAsync();

        var guardian = await WithDbAsync(db => db.Users.AsNoTracking().Include(u => u.Roles).SingleAsync(u => u.Email == "ouder@example.com"));
        Assert.Null(guardian.MemberId);
        var ouderRoleId = await WithDbAsync(db => db.Roles.Where(r => r.Code == DefaultRoles.Ouder).Select(r => r.Id).SingleAsync());
        Assert.Contains(guardian.Roles, r => r.RoleId == ouderRoleId);
        Assert.Equal(2, await WithDbAsync(db => db.GuardianRelations.CountAsync(g => g.GuardianUserId == guardian.Id)));
        Assert.Equal(2, await WithDbAsync(db => db.Members.CountAsync(m => m.LastName == "Berg")));
        Assert.Equal(2, _api.Emails.Sent.Count(m => m.To == "ouder@example.com" && m.Subject == "Welkom bij De Vrolijke Drammers"));

        // De ouder logt in en ziet beide kinderen; eigen lidgegevens heeft de ouder niet.
        var parent = _api.ClientFor(_api.Entra.SignUp("ouder@example.com"));
        var children = (await parent.GetFromJsonAsync<JsonElement>("/api/v1/me/children")).EnumerateArray().Select(c => c.GetProperty("fullName").GetString()).ToList();
        Assert.Equal(["Sanne van der Berg", "Tim van der Berg"], children);
        Assert.Equal(HttpStatusCode.Forbidden, (await parent.GetAsync("/api/v1/me/member")).StatusCode);
    }

    [Fact]
    public async Task Dansgarde_aanmelding_zet_groep_Dansgarde_bij_het_nieuwe_lid()
    {
        var id = await SubmitAsync(Form("ouder@example.com", 8, "Lot", guardian: "Robin Mom", membershipType: "Dansgarde"), "ouder@example.com");
        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/membership-applications/{id}");
        Assert.Equal("Dansgarde", detail.GetProperty("membershipType").GetString());

        await _bestuur.PostAsync($"/api/v1/admin/membership-applications/{id}/approve", null);
        await RunProvisioningAsync();

        var application = await WithDbAsync(db => db.MembershipApplications.AsNoTracking().SingleAsync(a => a.Id == id));
        var member = await WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.Id == application.ResultingMemberId));
        Assert.Equal("Dansgarde", member.ParadeGroupName);
    }

    [Fact]
    public async Task Minderjarige_zonder_ouder_ongeldig_IBAN_te_jong_of_geen_machtiging_worden_geweigerd()
    {
        foreach (var form in new[]
        {
            Form("a@example.com", 10),
            Form("b@example.com", 30, iban: "NL91ABNA0417164301"),
            Form("c@example.com", 3, guardian: "Ouder"),
            Form("d@example.com", 30, mandate: false),
        })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _anonymous.PostAsJsonAsync("/api/v1/membership-applications", form)).StatusCode);
        }

        Assert.Equal(0, await WithDbAsync(db => db.MembershipApplications.CountAsync()));
    }

    [Fact]
    public async Task Code_verloopt_heeft_maximaal_vijf_pogingen_en_een_nieuwe_code_werkt()
    {
        var start = await _anonymous.PostAsJsonAsync("/api/v1/membership-applications", Form("code@example.com", 30));
        var id = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var url = $"/api/v1/membership-applications/{id}/verify-email";

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _anonymous.PostAsJsonAsync(url, new { code = "000000" == LastCodeFor("code@example.com") ? "111111" : "000000" })).StatusCode);
        _api.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Conflict, (await _anonymous.PostAsJsonAsync(url, new { code = LastCodeFor("code@example.com") })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _anonymous.PostAsync($"/api/v1/membership-applications/{id}/resend-code", null)).StatusCode);
        var fresh = LastCodeFor("code@example.com");
        for (var i = 0; i < MembershipApplications.MaxCodeAttempts; i++)
        {
            await _anonymous.PostAsJsonAsync(url, new { code = fresh == "999999" ? "888888" : "999999" });
        }

        Assert.Equal(HttpStatusCode.Conflict, (await _anonymous.PostAsJsonAsync(url, new { code = fresh })).StatusCode);
        // Een onbevestigde aanmelding staat niet in de wachtrij.
        Assert.Equal(0, (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/membership-applications")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Afwijzen_vraagt_een_reden_wist_de_bankgegevens_en_is_definitief()
    {
        var id = await SubmitAsync(Form("nee@example.com", 30), "nee@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await _bestuur.PostAsJsonAsync($"/api/v1/admin/membership-applications/{id}/reject", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsJsonAsync($"/api/v1/admin/membership-applications/{id}/reject", new { reason = "Geen woonplaats in de regio" })).StatusCode);

        var application = await WithDbAsync(db => db.MembershipApplications.AsNoTracking().SingleAsync(a => a.Id == id));
        Assert.Equal((ApplicationStatus.Rejected, (string?)null), (application.Status, application.Iban));
        Assert.Equal(HttpStatusCode.Conflict, (await _bestuur.PostAsync($"/api/v1/admin/membership-applications/{id}/approve", null)).StatusCode);
        Assert.False(await WithDbAsync(db => db.Members.AnyAsync()));
    }

    [Fact]
    public async Task Met_schrijven_aan_gaat_het_lid_met_machtiging_naar_e_Boekhouden_en_herhalen_maakt_geen_tweede_lid()
    {
        await using var api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), configure: services =>
        {
            services.AddSingleton<IEBoekhoudenClient>(_eb);
            services.PostConfigure<EBoekhoudenOptions>(o => o.WriteEnabled = true);
        });
        var bestuur = api.ClientFor((await api.CreateUserAsync("bestuur2@example.com", DefaultRoles.Bestuur)).ObjectId);
        var start = await api.CreateClient().PostAsJsonAsync("/api/v1/membership-applications", Form("eb@example.com", 40));
        var id = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var code = CodePattern().Match(api.Emails.Sent.Last().PlainText).Value;
        await api.CreateClient().PostAsJsonAsync($"/api/v1/membership-applications/{id}/verify-email", new { code });
        await bestuur.PostAsync($"/api/v1/admin/membership-applications/{id}/approve", null);

        // De welkomstmail mislukt de eerste keer: de saga staat na "lid aangemaakt" en wordt hervat.
        api.Emails.FailNextSend = true;
        async Task RunAsync()
        {
            using var scope = api.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<MembershipApplications>().RunProvisioningAsync(id, CancellationToken.None);
            }
            catch (HttpRequestException)
            {
            }
        }

        await RunAsync();
        var failed = await bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/membership-applications/{id}");
        Assert.Equal("ProvisioningFailed", failed.GetProperty("status").GetString());
        Assert.Contains("ACS", failed.GetProperty("provisioning").GetProperty("lastError").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, (await bestuur.PostAsync($"/api/v1/admin/membership-applications/{id}/retry", null)).StatusCode);
        await RunAsync();

        var created = Assert.Single(_eb.Created);
        Assert.Equal(("Piet van der Berg", ValidIban, true, "D"), (created.Name, created.Iban, created.Mandate, created.MandateType));
        Assert.StartsWith("DVD-", created.MandateId, StringComparison.Ordinal);
        Assert.NotNull(created.MandateSignedDate);
        var done = await bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/membership-applications/{id}");
        Assert.Equal("Activated", done.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, done.GetProperty("ibanMasked").ValueKind);
    }

    [Fact]
    public async Task Adres_dat_al_bij_een_ander_lid_hoort_blokkeert_goedkeuren_voordat_er_iets_in_e_Boekhouden_komt()
    {
        // Bestaand lid met app-account op hetzelfde adres (zoals lid 0608 in Dev).
        var (userId, _) = await _api.CreateUserAsync("bezet@example.com", DefaultRoles.Lid);
        await WithDbAsync(async db =>
        {
            var member = new Drammers.Modules.Membership.Members.Member { Id = Guid.NewGuid(), MemberNumber = "0608", FullName = "Robin Bestaand" };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = member.Id;
            return await db.SaveChangesAsync();
        });
        var id = await SubmitAsync(Form("bezet@example.com", 30), "bezet@example.com");

        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/membership-applications/{id}");
        Assert.Equal("Robin Bestaand (lidnummer 0608)", detail.GetProperty("emailInUseBy").GetString());
        var approve = await _bestuur.PostAsync($"/api/v1/admin/membership-applications/{id}/approve", null);

        Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);
        Assert.Contains("hoort al bij het app-account van Robin Bestaand", (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(ApplicationStatus.Submitted, await WithDbAsync(db => db.MembershipApplications.Where(a => a.Id == id).Select(a => a.Status).SingleAsync()));
        Assert.Empty(_eb.Created);
    }

    [Fact]
    public async Task Alleen_met_member_approve()
    {
        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/membership-applications")).StatusCode);
    }

    [Fact]
    public void IBAN_controle()
    {
        Assert.Equal(ValidIban, MembershipApplications.NormalizeIban("nl91 abna 0417 1643 00"));
        Assert.Throws<Drammers.SharedKernel.Errors.DomainException>(() => MembershipApplications.NormalizeIban("NL91ABNA0417164301"));
        Assert.Equal("NL** **** **** 4300", MembershipApplications.MaskIban(ValidIban));
    }
}
