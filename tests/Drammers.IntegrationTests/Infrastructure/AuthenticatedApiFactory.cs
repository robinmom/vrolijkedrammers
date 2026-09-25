using System.Net.Http.Headers;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Identifiers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>API met echte database en tokenvalidatie (lokale testsleutel), ingesteld als de Dev-omgeving.</summary>
public sealed class AuthenticatedApiFactory(
    string connectionString, string? blobConnectionString = null, Action<IServiceCollection>? configure = null) : WebApplicationFactory<Program>
{
    public FakeEntraUserDirectory Entra { get; } = new();

    /// <summary>Instelbare klok (geplande publicatie, SAS-verloop).</summary>
    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Drammers", connectionString);
        builder.UseSetting("Worker:Enabled", "false");
        builder.UseSetting("Auth:Audience", TestTokens.DevAudience);
        builder.UseSetting("Auth:RequiredEnvironmentAccess", "dev");
        if (blobConnectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Blob", blobConnectionString);
        }
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEntraUserDirectory>(Entra);
            services.AddSingleton<Drammers.SharedKernel.Time.IClock>(Clock);
            configure?.Invoke(services);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
            });
        });
    }

    public HttpClient ClientFor(string objectId, string? environmentAccess = "dev")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(objectId, environmentAccess));
        return client;
    }

    /// <summary>Maakt een lokale gebruiker met rollen (zoals de provisioning dat doet) en geeft de <c>oid</c> terug.</summary>
    public async Task<(Guid UserId, string ObjectId)> CreateUserAsync(string email, params string[] roleCodes)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var objectId = Guid.NewGuid().ToString();
        var user = new User
        {
            Id = IdGenerator.NewId(),
            ExternalObjectId = objectId,
            Email = email,
            DisplayName = email,
            AccountStatus = AccountStatus.Active,
        };
        var roleIds = await db.Roles.Where(r => roleCodes.Contains(r.Code)).Select(r => r.Id).ToListAsync();
        user.Roles.AddRange(roleIds.Select(id => new UserRole { UserId = user.Id, RoleId = id, AssignedAt = DateTime.UtcNow }));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        Entra.AccountsByEmail[email] = objectId;
        return (user.Id, objectId);
    }

    public static string Admin => DefaultRoles.BeheerderIt;
}
