using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 26: gegevens wijzigen vanuit de app en een combinatie verbreken, altijd met goedkeuring.</summary>
[Collection(SqlServerCollection.Name)]
public class MemberRequestTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string Iban = "NL91ABNA0417164300";

    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }

    /// <summary>Lid met app-account; geeft lid-id en client.</summary>
    private async Task<(Guid MemberId, HttpClient Client)> MemberAsync(
        string number, string email, MembershipKind kind = MembershipKind.OnePerson, Guid? payer = null, short joinYear = 2001)
    {
        var (userId, oid) = await _api.CreateUserAsync(email, DefaultRoles.Lid);
        var id = await WithDbAsync(async db =>
        {
            var member = new Member
            {
                Id = IdGenerator.NewId(),
                MemberNumber = number,
                FullName = $"Lid {number}",
                FirstName = "Lid",
                Email = email,
                AddressLine = "Dorpsstraat 1",
                PostalCode = "6999 AA",
                City = "Loil",
                MembershipStatus = MembershipStatus.Active,
                MembershipKind = kind,
                PayerMemberId = payer,
                JoinYear = joinYear,
                BirthDate = new DateOnly(1980, 1, 1),
            };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = member.Id;
            await db.SaveChangesAsync();
            return member.Id;
        });
        return (id, _api.ClientFor(oid));
    }

    [Fact]
    public async Task Gegevens_wijzigen_pas_na_goedkeuring()
    {
        var (memberId, lid) = await MemberAsync("100", "lid100@example.com");

        var nothing = await lid.PostAsJsonAsync("/api/v1/me/change-requests", new { addressLine = "Dorpsstraat 1", mandateConsent = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nothing.StatusCode);
        var noMandate = await lid.PostAsJsonAsync("/api/v1/me/change-requests", new { iban = Iban, accountHolder = "L. Lid", mandateConsent = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noMandate.StatusCode);

        var submit = await lid.PostAsJsonAsync("/api/v1/me/change-requests", new
        {
            addressLine = "Kerkstraat 5",
            postalCode = "6999 ab",
            city = "Loil",
            email = "Nieuw100@Example.com",
            iban = "nl91 abna 0417 1643 00",
            accountHolder = "L. Lid",
            mandateConsent = true,
        });
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);

        // Nog niets doorgevoerd.
        Assert.Equal("Dorpsstraat 1", (await WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId))).AddressLine);
        var mine = await lid.GetFromJsonAsync<JsonElement>("/api/v1/me/membership-requests");
        var latest = mine.GetProperty("latestChange");
        Assert.Equal("Pending", latest.GetProperty("status").GetString());
        Assert.Equal(["Adres", "E-mailadres", "IBAN"], latest.GetProperty("fields").EnumerateArray().Select(f => f.GetString()));

        var overview = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/member-requests");
        var change = overview.GetProperty("changes").EnumerateArray().Single();
        var fields = change.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("field").GetString()!, f => f.GetProperty("requested").GetString());
        Assert.Equal(("Kerkstraat 5", "6999 AB", "nieuw100@example.com"), (fields["address"], fields["postalCode"], fields["email"]));
        Assert.StartsWith("**** 4300", fields["iban"]);
        Assert.DoesNotContain("NL91", change.GetRawText(), StringComparison.Ordinal);

        var id = change.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/member-requests/changes/{id}/approve", null)).StatusCode);
        var member = await WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId));
        Assert.Equal(("Kerkstraat 5", "nieuw100@example.com", "4300", "L. Lid"), (member.AddressLine, member.Email, member.IbanLast4, member.AccountHolder));
        Assert.Contains("address", member.LocalFields!, StringComparison.Ordinal);
        Assert.NotNull(member.MandateReference);
        using (var scope = _api.Services.CreateScope())
        {
            Assert.Equal(Iban, scope.ServiceProvider.GetRequiredService<MemberIbanProtector>().Unprotect(member.IbanProtected!));
        }

        Assert.Contains(_api.Emails.Sent, m => m.To == "nieuw100@example.com" && m.Subject == "Je gegevens zijn bijgewerkt");

        // Afwijzen: niets doorgevoerd, IBAN niet bewaard.
        await lid.PostAsJsonAsync("/api/v1/me/change-requests", new { phone = "0611111111", mandateConsent = false });
        var second = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/member-requests")).GetProperty("changes").EnumerateArray().Single().GetProperty("id").GetGuid();
        var reject = await _bestuur.PostAsJsonAsync($"/api/v1/admin/member-requests/changes/{second}/reject", new { reason = "Klopt niet" });
        Assert.Equal(HttpStatusCode.NoContent, reject.StatusCode);
        Assert.Null((await WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.Id == memberId))).Phone);
        Assert.Equal("Rejected", (await lid.GetFromJsonAsync<JsonElement>("/api/v1/me/membership-requests")).GetProperty("latestChange").GetProperty("status").GetString());

        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/member-requests")).StatusCode);
    }

    [Fact]
    public async Task Combinatie_verbreken_met_beide_akkoord_en_goedkeuring()
    {
        var (payerId, payer) = await MemberAsync("200", "betaler@example.com", MembershipKind.TwoPersons, joinYear: 1993);
        var (partnerId, partner) = await MemberAsync("201", "partner@example.com", MembershipKind.Partner, payerId, joinYear: 1993);
        var (_, single) = await MemberAsync("202", "los@example.com");

        Assert.Equal(JsonValueKind.Null, (await single.GetFromJsonAsync<JsonElement>("/api/v1/me/membership-requests")).GetProperty("combination").ValueKind);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await single.PostAsJsonAsync("/api/v1/me/combination-break", new { mandateConsent = false })).StatusCode);

        // Het tweede lid gaat zelf betalen: zonder IBAN kan het niet.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await partner.PostAsJsonAsync("/api/v1/me/combination-break", new { mandateConsent = false })).StatusCode);
        var start = await partner.PostAsJsonAsync("/api/v1/me/combination-break", new { iban = Iban, accountHolder = "P. Partner", mandateConsent = true });
        Assert.Equal(HttpStatusCode.NoContent, start.StatusCode);
        Assert.Contains(_api.Emails.Sent, m => m.To == "betaler@example.com" && m.Subject.Contains("verbreken", StringComparison.Ordinal));

        var payerView = (await payer.GetFromJsonAsync<JsonElement>("/api/v1/me/membership-requests")).GetProperty("combination");
        Assert.Equal(("Payer", "AwaitingAgreement", false, true, "Lid 201"), (payerView.GetProperty("role").GetString(), payerView.GetProperty("breakStatus").GetString(),
            payerView.GetProperty("iAgreed").GetBoolean(), payerView.GetProperty("otherAgreed").GetBoolean(), payerView.GetProperty("otherName").GetString()));

        // De ledenadministratie kan pas na beide akkoorden goedkeuren.
        var pending = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/member-requests")).GetProperty("breaks").EnumerateArray().Single();
        var requestId = pending.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await _bestuur.PostAsync($"/api/v1/admin/member-requests/breaks/{requestId}/approve", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await payer.PostAsJsonAsync("/api/v1/me/combination-break/agree", new { mandateConsent = false })).StatusCode);
        var waiting = (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/member-requests")).GetProperty("breaks").EnumerateArray().Single();
        Assert.Equal(("AwaitingApproval", "**** 4300"), (waiting.GetProperty("status").GetString(), waiting.GetProperty("partnerIbanMasked").GetString()));

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/member-requests/breaks/{requestId}/approve", null)).StatusCode);
        var (p, q) = await WithDbAsync(async db => (
            await db.Members.AsNoTracking().SingleAsync(m => m.Id == payerId),
            await db.Members.AsNoTracking().SingleAsync(m => m.Id == partnerId)));
        Assert.Equal((MembershipKind.OnePerson, MembershipKind.OnePerson, (Guid?)null), (p.MembershipKind, q.MembershipKind, q.PayerMemberId));
        Assert.Equal(("4300", (short?)1993), (q.IbanLast4, q.JoinYear));

        // Beiden betalen nu het tarief voor één lid.
        var contributions = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/contributions?date=2027-03-01");
        decimal Amount(string number) => contributions.GetProperty("lines").EnumerateArray()
            .Single(l => l.GetProperty("memberNumber").GetString() == number).GetProperty("amount").GetDecimal();
        Assert.Equal((32.50m, 32.50m), (Amount("200"), Amount("201")));
        Assert.Equal(JsonValueKind.Null, (await partner.GetFromJsonAsync<JsonElement>("/api/v1/me/membership-requests")).GetProperty("combination").ValueKind);
    }

    [Fact]
    public async Task Niet_akkoord_trekt_het_verzoek_in()
    {
        var (payerId, payer) = await MemberAsync("300", "b300@example.com", MembershipKind.TwoPersons);
        var (_, partner) = await MemberAsync("301", "p301@example.com", MembershipKind.Partner, payerId);

        Assert.Equal(HttpStatusCode.NoContent, (await payer.PostAsJsonAsync("/api/v1/me/combination-break", new { mandateConsent = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await partner.PostAsJsonAsync("/api/v1/me/combination-break", new { iban = Iban, accountHolder = "P", mandateConsent = true })).StatusCode);
        var view = (await partner.GetFromJsonAsync<JsonElement>("/api/v1/me/membership-requests")).GetProperty("combination");
        Assert.True(view.GetProperty("ibanRequiredFromMe").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await partner.PostAsync("/api/v1/me/combination-break/cancel", null)).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await payer.GetFromJsonAsync<JsonElement>("/api/v1/me/membership-requests")).GetProperty("combination").GetProperty("breakStatus").ValueKind);
        Assert.Equal(MembershipKind.TwoPersons, (await WithDbAsync(db => db.Members.AsNoTracking().SingleAsync(m => m.Id == payerId))).MembershipKind);
    }
}
