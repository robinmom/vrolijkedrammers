using Drammers.Infrastructure.Auditing;
using Drammers.Infrastructure.Persistence;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.SharedKernel.Auditing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public class AuditLogTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Gelijktijdige_auditregels_vormen_één_geldige_hash_keten()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        await using var provider = TestServices.Create(connectionString, new FakeClock(DateTimeOffset.UtcNow));

        await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAuditLogger>()
                .WriteAsync(new AuditEntry("test.action", "Test", i.ToString(System.Globalization.CultureInfo.InvariantCulture), NewValues: "{\"n\":1}"));
        }));

        var entries = await LoadChainAsync(provider);
        Assert.Equal(12, entries.Count);
        Assert.Null(AuditChain.FindFirstBrokenEntry(entries));
    }

    [Fact]
    public async Task Gewijzigde_regel_breekt_de_keten()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        await using var provider = TestServices.Create(connectionString, new FakeClock(DateTimeOffset.UtcNow));
        for (var i = 0; i < 3; i++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IAuditLogger>().WriteAsync(new AuditEntry("test.action", "Test", $"{i}"));
        }

        // Een beheerder met volledige rechten (niet de runtime) past regel 2 aan.
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var tamper = new SqlCommand("UPDATE audit.AuditLog SET entity_id = 'vervalst' WHERE id = 2", connection);
            await tamper.ExecuteNonQueryAsync();
        }

        Assert.Equal(2, AuditChain.FindFirstBrokenEntry(await LoadChainAsync(provider)));
    }

    private static async Task<List<Modules.Audit.AuditLog.AuditLogEntry>> LoadChainAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().AuditLog.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
    }
}
