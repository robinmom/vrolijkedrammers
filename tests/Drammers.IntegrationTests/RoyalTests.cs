using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Prins(es) en adjudanten (2026-10-10): maximum per rol, begroeting en informatie in de app.</summary>
[Collection(SqlServerCollection.Name)]
public class RoyalTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    /// <summary>Een lid met een account (rol Lid).</summary>
    private async Task<(Guid UserId, HttpClient Client)> MemberAsync(string number, string first, string gender)
    {
        var (userId, oid) = await _api.CreateUserAsync($"{first.ToLowerInvariant()}@example.com", DefaultRoles.Lid);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var member = new Member
        {
            Id = IdGenerator.NewId(),
            MemberNumber = number,
            FullName = $"{first} Drammer",
            FirstName = first,
            LastName = "Drammer",
            Gender = gender,
            MembershipStatus = MembershipStatus.Active,
        };
        db.Members.Add(member);
        await db.SaveChangesAsync();
        (await db.Users.SingleAsync(u => u.Id == userId)).MemberId = member.Id;
        await db.SaveChangesAsync();
        return (userId, _api.ClientFor(oid));
    }

    private Task<HttpResponseMessage> AssignAsync(Guid userId, string role) =>
        _bestuur.PutAsJsonAsync($"/api/v1/admin/users/{userId}/roles", new
        {
            roles = new object[] { new { roleCode = DefaultRoles.Lid }, new { roleCode = role, validTo = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(5) } },
        });

    [Fact]
    public async Task Een_prinses_twee_adjudanten_en_hun_begroeting()
    {
        var (anna, annaClient) = await MemberAsync("1", "Anna", "v");
        var (bram, bramClient) = await MemberAsync("2", "Bram", "m");
        var (chris, _) = await MemberAsync("3", "Chris", "m");
        var (dirk, _) = await MemberAsync("4", "Dirk", "m");
        var (_, gewoon) = await MemberAsync("5", "Eva", "v");

        Assert.Equal(HttpStatusCode.NoContent, (await AssignAsync(anna, DefaultRoles.Prins)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await AssignAsync(bram, DefaultRoles.Adjudant)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await AssignAsync(chris, DefaultRoles.Adjudant)).StatusCode);

        // Maximaal één prins(es) en twee adjudanten.
        Assert.Equal(HttpStatusCode.Conflict, (await AssignAsync(dirk, DefaultRoles.Adjudant)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AssignAsync(dirk, DefaultRoles.Prins)).StatusCode);

        // Informatie per rol, in Markdown.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync("/api/v1/admin/royal/info/prins", new { body = "**Proclamatie** op 11-11" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync("/api/v1/admin/royal/info/adjudant", new { body = "Haal de prinses op om 19:00" })).StatusCode);
        var overview = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/royal");
        Assert.Equal(3, overview.GetProperty("holders").GetArrayLength());

        var prinses = await annaClient.GetFromJsonAsync<JsonElement>("/api/v1/me/royal");
        Assert.Equal("Welkom prinses Anna", prinses.GetProperty("greeting").GetString());
        Assert.Contains("<strong>Proclamatie</strong>", prinses.GetProperty("infoHtml").GetString());
        var adjudant = await bramClient.GetFromJsonAsync<JsonElement>("/api/v1/me/royal");
        Assert.Equal("Welkom adjudant Bram van prinses Anna", adjudant.GetProperty("greeting").GetString());
        Assert.Contains("Haal de prinses op", adjudant.GetProperty("infoHtml").GetString());

        // Iemand zonder de rol: niets.
        Assert.Equal(HttpStatusCode.NoContent, (await gewoon.GetAsync("/api/v1/me/royal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gewoon.GetAsync("/api/v1/admin/royal")).StatusCode);
    }
}
