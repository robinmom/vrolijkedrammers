using Drammers.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Drammers.IntegrationTests;

/// <summary>De runtime-rol (API-identiteit) heeft geen DDL en kan de auditlog niet wijzigen (fase 2-security).</summary>
[Collection(SqlServerCollection.Name)]
public class PermissionTests(SqlServerFixture sql) : IAsyncLifetime
{
    private SqlConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqlConnection(await sql.CreateMigratedDatabaseAsync());
        await _connection.OpenAsync();
        await ExecuteAsync("""
            CREATE USER runtime_test WITHOUT LOGIN;
            ALTER ROLE app_runtime ADD MEMBER runtime_test;
            INSERT INTO audit.AuditLog (occurred_at, actor_type, action, entity_type, entity_id, hash)
            VALUES (SYSUTCDATETIME(), 'System', 'test.created', 'Test', '1', REPLICATE('a', 64));
            """);
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public Task Runtime_mag_auditregels_toevoegen_en_lezen() =>
        AsRuntimeAsync("""
            INSERT INTO audit.AuditLog (occurred_at, actor_type, action, entity_type, entity_id, hash)
            VALUES (SYSUTCDATETIME(), 'System', 'test.runtime', 'Test', '2', REPLICATE('b', 64));
            SELECT COUNT(*) FROM audit.AuditLog;
            """);

    [Theory]
    [InlineData("UPDATE audit.AuditLog SET action = 'gewijzigd'")]
    [InlineData("DELETE FROM audit.AuditLog")]
    [InlineData("TRUNCATE TABLE audit.AuditLog")]
    [InlineData("ALTER TABLE audit.AuditLog DROP CONSTRAINT CK_AuditLog_actor_type")]
    public async Task Runtime_kan_de_auditlog_niet_wijzigen_of_verwijderen(string statement)
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => AsRuntimeAsync(statement));
        Assert.Contains(ex.Number, new[] { 229, 1088, 4902, 3701, 15247 });
        Assert.Equal(1, await MigrationTests.ScalarAsync<int>(_connection, "SELECT COUNT(*) FROM audit.AuditLog WHERE action = 'test.created'"));
    }

    [Fact]
    public async Task Runtime_heeft_geen_DDL()
    {
        var ex = await Assert.ThrowsAsync<SqlException>(() => AsRuntimeAsync("CREATE TABLE content.Hack (id int)"));
        Assert.Equal(262, ex.Number);
    }

    [Fact]
    public Task Runtime_mag_gegevens_in_moduleschemas_wijzigen() =>
        AsRuntimeAsync("UPDATE config.AppConfiguration SET value = 'true' WHERE [key] = 'maintenance_mode'");

    private async Task AsRuntimeAsync(string statement)
    {
        await ExecuteAsync("EXECUTE AS USER = 'runtime_test';");
        try
        {
            await ExecuteAsync(statement);
        }
        finally
        {
            await ExecuteAsync("REVERT;");
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = new SqlCommand(sql, _connection);
        await command.ExecuteNonQueryAsync();
    }
}
