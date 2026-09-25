using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Setup;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.Azurite;
using Testcontainers.MsSql;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>
/// Eén SQL Server-container per testrun; elke testklasse krijgt een eigen database, opgebouwd met hetzelfde
/// idempotente migratiescript als de deploy-pipeline.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest").WithInMemoryPersistence().Build();

    public string ServerConnectionString => _container.GetConnectionString();

    /// <summary>Blob Storage-emulator; elke testklasse maakt eigen containers met <see cref="CreateBlobStorageAsync"/>.</summary>
    public string BlobConnectionString => _azurite.GetConnectionString();

    public Task InitializeAsync() => Task.WhenAll(_container.StartAsync(), _azurite.StartAsync());

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    /// <summary>Maakt de containers aan (idempotent) zoals Bicep dat in Azure doet.</summary>
    public async Task<string> CreateBlobStorageAsync()
    {
        var service = new Azure.Storage.Blobs.BlobServiceClient(BlobConnectionString);
        foreach (var container in Drammers.Infrastructure.Files.FileContainers.All)
        {
            await service.GetBlobContainerClient(container).CreateIfNotExistsAsync();
        }

        return BlobConnectionString;
    }

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
