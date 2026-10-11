using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Eén e-mailadres = één account (2026-10-11): ouder en kind met hetzelfde adres, en twee volwassenen.</summary>
[Collection(SqlServerCollection.Name)]
public class SharedEmailTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    /// <summary>Een lid met een account dat nog nooit heeft ingelogd.</summary>
    private async Task<(Guid MemberId, Guid UserId)> MemberWithPendingAccountAsync(
        string number, string first, string email, DateOnly? birthDate, MembershipKind? kind = null, string? ebStatus = null)
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var member = new Member
        {
            Id = IdGenerator.NewId(),
            MemberNumber = number,
            FullName = $"{first} Berendsen",
            FirstName = first,
            LastName = "Berendsen",
            Email = email,
            BirthDate = birthDate,
            MembershipKind = kind,
            EbStatusRaw = ebStatus,
            MembershipStatus = MembershipStatus.Active,
        };
        db.Members.Add(member);
        var lid = await db.Roles.Where(r => r.Code == DefaultRoles.Lid).Select(r => r.Id).SingleAsync();
        var user = new User
        {
            Id = IdGenerator.NewId(),
            ExternalObjectId = PendingObjectId.New(),
            Email = email,
            DisplayName = member.FullName,
            AccountStatus = AccountStatus.Active,
            MemberId = member.Id,
        };
        user.Roles.Add(new UserRole { UserId = user.Id, RoleId = lid, AssignedAt = DateTime.UtcNow });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (member.Id, user.Id);
    }

    [Fact]
    public async Task Ouder_en_dansgardekind_zonder_geboortedatum_een_account_bij_de_eerste_keer_inloggen()
    {
        const string Email = "anouk@example.com";
        var (anouk, anoukUser) = await MemberWithPendingAccountAsync("631", "Anouk", Email, new DateOnly(1982, 6, 18), MembershipKind.Partner);
        var (lola, _) = await MemberWithPendingAccountAsync("640", "Lola", Email, null, ebStatus: "Lidmaatschap dansgarde DVD");

        // Portal: het systeem weet wat het doet.
        var conflict = Assert.Single((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/shared-email-accounts")).EnumerateArray());
        Assert.True(conflict.GetProperty("resolvable").GetBoolean());
        Assert.Equal("Anouk Berendsen", conflict.GetProperty("keepName").GetString());
        Assert.Equal(["Lola Berendsen"], conflict.GetProperty("children").EnumerateArray().Select(c => c.GetString()));

        // Eerste keer inloggen: het account van Anouk, met Lola onder Mijn kinderen.
        var anoukClient = _api.ClientFor(_api.Entra.SignUp(Email));
        var me = await anoukClient.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(anouk, me.GetProperty("memberId").GetGuid());
        var child = Assert.Single((await anoukClient.GetFromJsonAsync<JsonElement>("/api/v1/me/children")).EnumerateArray());
        Assert.Equal(lola, child.GetProperty("memberId").GetGuid());

        Assert.Equal(1, await CountActiveAsync(Email));
        Assert.Equal(anoukUser, await IdAsync(Email));
        Assert.Empty((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/shared-email-accounts")).EnumerateArray());
    }

    [Fact]
    public async Task Twee_volwassenen_met_hetzelfde_adres_kiest_het_systeem_niet()
    {
        const string Email = "samen@example.com";
        await MemberWithPendingAccountAsync("700", "Kees", Email, new DateOnly(1970, 1, 1));
        await MemberWithPendingAccountAsync("701", "Ria", Email, new DateOnly(1972, 1, 1));

        var conflict = Assert.Single((await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/shared-email-accounts")).EnumerateArray());
        Assert.False(conflict.GetProperty("resolvable").GetBoolean());
        Assert.Contains("eigen e-mailadres", conflict.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await _bestuur.PostAsJsonAsync("/api/v1/admin/shared-email-accounts/resolve", new { email = Email })).StatusCode);

        var client = _api.ClientFor(_api.Entra.SignUp(Email));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(2, await CountActiveAsync(Email));
    }

    [Fact]
    public async Task Samenvoegen_vanuit_het_portal()
    {
        const string Email = "gezin@example.com";
        await MemberWithPendingAccountAsync("800", "Mark", Email, new DateOnly(1980, 3, 3));
        await MemberWithPendingAccountAsync("801", "Sem", Email, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-9));

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsJsonAsync("/api/v1/admin/shared-email-accounts/resolve", new { email = Email })).StatusCode);
        Assert.Equal(1, await CountActiveAsync(Email));
        Assert.Equal(1, await WithDbAsync(db => db.GuardianRelations.CountAsync()));
    }

    private Task<int> CountActiveAsync(string email) =>
        WithDbAsync(db => db.Users.CountAsync(u => u.Email == email && u.AccountStatus != AccountStatus.Deleted));

    private Task<Guid> IdAsync(string email) =>
        WithDbAsync(db => db.Users.Where(u => u.Email == email && u.AccountStatus != AccountStatus.Deleted).Select(u => u.Id).SingleAsync());

    private async Task<T> WithDbAsync<T>(Func<DrammersDbContext, Task<T>> action)
    {
        using var scope = _api.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DrammersDbContext>());
    }
}
