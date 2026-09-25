using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Setup;
using Drammers.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Drammers.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public class MigrationTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Migraties_van_leeg_naar_laatste_versie_en_idempotent_opnieuw()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Nogmaals hetzelfde script: mag niets doen en niet falen.
        await SqlScriptRunner.RunAsync(connection, SqlServerFixture.MigrationScript(), CancellationToken.None);

        using var context = new DrammersDbContext(new DbContextOptionsBuilder<DrammersDbContext>().UseSqlServer(connectionString).Options);
        Assert.Equal(context.Database.GetMigrations().Count(), await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [__EFMigrationsHistory]"));
        Assert.Equal("2026/2027", await ScalarAsync<string>(connection, "SELECT name FROM content.CarnivalYear WHERE active = 1"));
        Assert.Equal(13, await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM config.RetentionPolicy"));
        Assert.Equal(11, await ScalarAsync<int>(
            connection,
            "SELECT COUNT(*) FROM sys.schemas WHERE name IN ('identity','membership','content','notification','ticketing','payments','parade','import','audit','config','reporting')"));
        Assert.Equal(3, await ScalarAsync<int>(
            connection, "SELECT COUNT(*) FROM sys.database_principals WHERE type = 'R' AND name IN ('app_runtime','app_migrator','app_reporting')"));
    }

    [Fact]
    public async Task Enumkolommen_hebben_een_CHECK_constraint()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var ex = await Assert.ThrowsAsync<SqlException>(() => ScalarAsync<int>(
            connection, "INSERT INTO config.RetentionPolicy (data_type, retention_days, action, created_at) VALUES ('x', 1, 'Onbekend', SYSUTCDATETIME()); SELECT 1"));
        Assert.Equal(547, ex.Number);
    }

    internal static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
