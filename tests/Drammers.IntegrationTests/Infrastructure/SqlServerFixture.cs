using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Setup;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.MsSql;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>
/// Eén SQL Server-container per testrun; elke testklasse krijgt een eigen database, opgebouwd met hetzelfde
/// idempotente migratiescript als de deploy-pipeline.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ServerConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Het idempotente script zoals <c>dotnet ef migrations script --idempotent</c> dat maakt.</summary>
    public static string MigrationScript()
    {
        using var db = new DrammersDbContext(new DbContextOptionsBuilder<DrammersDbContext>().UseSqlServer("Server=unused").Options);
        return db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
    }

    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var name = $"drammers_{Guid.NewGuid():N}";
        await using (var master = new SqlConnection(ServerConnectionString))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{name}]", master);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new SqlConnectionStringBuilder(ServerConnectionString) { InitialCatalog = name }.ConnectionString;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await SqlScriptRunner.RunAsync(connection, MigrationScript(), CancellationToken.None);
        return connectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "sql-server";
}
