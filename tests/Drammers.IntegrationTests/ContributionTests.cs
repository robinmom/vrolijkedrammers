using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 23a: lidmaatschappen en contributie, met de tarieven van 2026 uit de seed.</summary>
[Collection(SqlServerCollection.Name)]
public class ContributionTests(SqlServerFixture sql) : IAsyncLifetime
{
    private static readonly DateOnly IncassoDate = new(2027, 3, 1);

    private AuthenticatedApiFactory _api = null!;
    private HttpClient _penningmeester = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _penningmeester = _api.ClientFor((await _api.CreateUserAsync("bestuur-contributie@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> MemberAsync(
        string number, string? ebStatus, DateOnly? birthDate = null, MembershipKind? kind = null, MembershipStatus status = MembershipStatus.Active)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var member = new Member
        {
            Id = IdGenerator.NewId(),
            MemberNumber = number,
            FullName = $"Lid {number}",
            LastName = $"Lid {number}",
            MembershipStatus = status,
            EbStatusRaw = ebStatus,
            BirthDate = birthDate,
            MembershipKind = kind,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private async Task<JsonElement> OverviewAsync() =>
        await _penningmeester.GetFromJsonAsync<JsonElement>($"/api/v1/admin/contributions?date={IncassoDate:yyyy-MM-dd}");

    private static JsonElement Line(JsonElement overview, string number) =>
        overview.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("memberNumber").GetString() == number);

    private static (decimal Amount, string Status, bool Senior) Summary(JsonElement line) =>
        (line.GetProperty("amount").GetDecimal(), line.GetProperty("status").GetString()!, line.GetProperty("senior").GetBoolean());

    [Fact]
    public async Task Contributie_per_soort_met_senioren_op_de_incassodatum()
    {
        await MemberAsync("1", "Eénpersoonslid DVD", new DateOnly(1990, 5, 1));
        // Wordt 65 op de dag van de incasso: seniorentarief.
        await MemberAsync("2", "Eénpersoonslid DVD", new DateOnly(1962, 3, 1));
        // Wordt 65 een dag later: nog het gewone tarief.
        await MemberAsync("3", "Eénpersoonslid DVD", new DateOnly(1962, 3, 2));
        await MemberAsync("4", "Lidmaatschap dansgarde DVD", new DateOnly(1950, 1, 1));
        await MemberAsync("5", null);
        await MemberAsync("6", "Eénpersoonslid DVD", status: MembershipStatus.Inactive);

        var overview = await OverviewAsync();
        Assert.Equal(32.50m, overview.GetProperty("rate").GetProperty("onePerson").GetDecimal());
        Assert.Equal((32.50m, "Due", false), Summary(Line(overview, "1")));
        Assert.Equal((22.00m, "Due", true), Summary(Line(overview, "2")));
        Assert.Equal((32.50m, "Due", false), Summary(Line(overview, "3")));
        Assert.Equal((85.00m, "Due", false), Summary(Line(overview, "4")));
        Assert.Equal((0m, "Unknown", false), Summary(Line(overview, "5")));
        Assert.DoesNotContain(overview.GetProperty("lines").EnumerateArray(), l => l.GetProperty("memberNumber").GetString() == "6");
        Assert.Equal(32.50m + 22.00m + 32.50m + 85.00m, overview.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Twee_personen_met_partner_en_convent_vrijstelling()
    {
        var payer = await MemberAsync("10", "Tweepersoonslid DVD", new DateOnly(1955, 1, 1));
        var partner = await MemberAsync("11", null, new DateOnly(1970, 1, 1));
        var convent = await MemberAsync("12", "Eénpersoonslid DVD", new DateOnly(1950, 1, 1));

        // Zonder gekoppelde partner: gewoon tweepersoonstarief met een melding.
        var line = Line(await OverviewAsync(), "10");
        Assert.Equal((57.50m, "Due", false), Summary(line));
        Assert.Contains("Tweede persoon", line.GetProperty("note").GetString());

        var put = await _penningmeester.PutAsJsonAsync($"/api/v1/admin/contributions/members/{partner}", new { kind = "Partner", payerMemberId = payer, exempt = false });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var overview = await OverviewAsync();
        // Partner is nog geen 65: beiden moeten 65+ zijn.
        Assert.Equal((57.50m, "Due", false), Summary(Line(overview, "10")));
        Assert.Equal("Lid 11", Line(overview, "10").GetProperty("partnerName").GetString());
        Assert.Equal((0m, "PaidByPartner", false), Summary(Line(overview, "11")));

        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            (await db.Members.FindAsync(partner))!.BirthDate = new DateOnly(1960, 1, 1);
            await db.SaveChangesAsync();
        }

        Assert.Equal((44.00m, "Due", true), Summary(Line(await OverviewAsync(), "10")));

        // Een tweede partner bij dezelfde betaler kan niet; de betaler kan niet van soort wisselen zolang hij een partner heeft.
        var other = await MemberAsync("13", null);
        var second = await _penningmeester.PutAsJsonAsync($"/api/v1/admin/contributions/members/{other}", new { kind = "Partner", payerMemberId = payer, exempt = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var change = await _penningmeester.PutAsJsonAsync($"/api/v1/admin/contributions/members/{payer}", new { kind = "OnePerson", exempt = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, change.StatusCode);

        var exempt = await _penningmeester.PutAsJsonAsync($"/api/v1/admin/contributions/members/{convent}",
            new { kind = "OnePerson", exempt = true, exemptReason = "Convent (bewezen dienstjaren)" });
        Assert.Equal(HttpStatusCode.NoContent, exempt.StatusCode);
        var conventLine = Line(await OverviewAsync(), "12");
        Assert.Equal((0m, "Exempt", true), Summary(conventLine));
        Assert.Equal("Convent (bewezen dienstjaren)", conventLine.GetProperty("note").GetString());

        var settings = await _penningmeester.GetFromJsonAsync<JsonElement>($"/api/v1/admin/contributions/members/{partner}");
        Assert.Equal(("Partner", payer), (settings.GetProperty("kind").GetString(), settings.GetProperty("payerMemberId").GetGuid()));
    }

    [Fact]
    public async Task Tarieven_per_ingangsdatum_en_rechten()
    {
        await MemberAsync("20", "Eénpersoonslid DVD", new DateOnly(1990, 1, 1));
        var put = await _penningmeester.PutAsJsonAsync("/api/v1/admin/contributions/rates", new
        {
            validFrom = "2027-01-01",
            onePerson = 35.00m,
            twoPersons = 60.00m,
            onePersonSenior = 24.00m,
            twoPersonsSenior = 46.00m,
            dansgarde = 90.00m,
        });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(35.00m, Line(await OverviewAsync(), "20").GetProperty("amount").GetDecimal());
        var before = await _penningmeester.GetFromJsonAsync<JsonElement>("/api/v1/admin/contributions?date=2026-12-31");
        Assert.Equal(32.50m, Line(before, "20").GetProperty("amount").GetDecimal());

        var invalid = await _penningmeester.PutAsJsonAsync("/api/v1/admin/contributions/rates", new
        {
            validFrom = "2028-01-01",
            onePerson = -1m,
            twoPersons = 60m,
            onePersonSenior = 24m,
            twoPersonsSenior = 46m,
            dansgarde = 90m,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);

        var tooEarly = await _penningmeester.GetAsync("/api/v1/admin/contributions?date=2025-06-01");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooEarly.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await _penningmeester.GetAsync("/api/v1/admin/contributions/export")).StatusCode);

        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await redactie.GetAsync("/api/v1/admin/contributions")).StatusCode);
    }
}
