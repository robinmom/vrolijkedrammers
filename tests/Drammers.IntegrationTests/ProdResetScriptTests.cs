using Drammers.Infrastructure.Setup;
using Drammers.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Drammers.IntegrationTests;

/// <summary>
/// infra/prod/reset-testdata.sql (eenmalig na de kopie Dev → Prod): klopt het script met het huidige schema, laat de
/// proefrun alles staan en haalt vastleggen de testgegevens weg zonder de rest te raken.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ProdResetScriptTests(SqlServerFixture sql)
{
    private static string Script(int apply, string name = "reset-testdata.sql")
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Drammers.sln")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "infra", "prod", name)).Replace("$(APPLY)", apply.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Proefrun_laat_alles_staan_en_vastleggen_haalt_de_testgegevens_weg()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var messages = new List<string>();
        connection.InfoMessage += (_, e) => messages.AddRange(e.Errors.Cast<SqlError>().Select(err => err.Message));

        await ExecAsync(connection, """
            INSERT INTO membership.Advertiser (id, number, company_name, kind, payment, active, added_via_app, created_at)
            VALUES (NEWID(), 1, 'Bakkerij De Test', 'Advertisement', 'Cash', 1, 0, SYSUTCDATETIME());
            INSERT INTO membership.AdvertiserYear (advertiser_id, year, status, is_free)
            SELECT id, 2027, 'Collected', 0 FROM membership.Advertiser;
            """);

        await SqlScriptRunner.RunAsync(connection, Script(0), CancellationToken.None);
        Assert.Equal(1, await MigrationTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM membership.Advertiser"));
        Assert.Contains(messages, m => m.StartsWith("PROEFRUN", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.StartsWith("Adverteerders:", StringComparison.Ordinal) && m.EndsWith(" 1", StringComparison.Ordinal));

        await SqlScriptRunner.RunAsync(connection, Script(1), CancellationToken.None);
        Assert.Equal(0, await MigrationTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM membership.Advertiser"));
        Assert.Equal(0, await MigrationTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM membership.AdvertiserYear"));
        Assert.Contains(messages, m => m == "VASTGELEGD.");

        // Instellingen en referentiegegevens blijven.
        Assert.Equal("2026/2027", await MigrationTests.ScalarAsync<string>(connection, "SELECT name FROM content.CarnivalYear WHERE active = 1"));
        Assert.True(await MigrationTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [identity].[Role]") > 0);
    }

    [Fact]
    public async Task Meldingen_uit_de_testperiode_weghalen()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var messages = new List<string>();
        connection.InfoMessage += (_, e) => messages.AddRange(e.Errors.Cast<SqlError>().Select(err => err.Message));

        await SqlScriptRunner.RunAsync(connection, Script(0, "reset-meldingen.sql"), CancellationToken.None);
        Assert.Contains(messages, m => m.StartsWith("PROEFRUN", StringComparison.Ordinal));
        await SqlScriptRunner.RunAsync(connection, Script(1, "reset-meldingen.sql"), CancellationToken.None);
        Assert.Contains(messages, m => m == "VASTGELEGD.");
        Assert.Equal(0, await MigrationTests.ScalarAsync<int>(connection, "SELECT COUNT(*) FROM notification.Notification"));
    }

    private static async Task ExecAsync(SqlConnection connection, string sqlText)
    {
        await using var command = new SqlCommand(sqlText, connection);
        await command.ExecuteNonQueryAsync();
    }
}
