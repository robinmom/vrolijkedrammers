using Microsoft.Data.SqlClient;

namespace Drammers.Infrastructure.Persistence;

/// <summary>
/// Verbindingen met een serverless database die kan pauzeren (Dev: gratis tegoed). Tijdens het opstarten geeft Azure SQL
/// direct fout 40613 ("not currently available"); dan opnieuw proberen, tot ruim een minuut. Alleen het openen van de
/// verbinding wordt herhaald, niet het uitvoeren van opdrachten: een wijziging wordt dus nooit dubbel opgeslagen.
/// </summary>
public static class SqlConnectionFactory
{
    /// <summary>Fouten bij het openen die "later opnieuw" betekenen (Azure SQL, gepauzeerd/herstartend/druk).</summary>
    public static readonly int[] TransientOpenErrors = [40613, 40197, 40501, 40540, 40143, 49918, 49919, 49920, 4060, 4221, 10928, 10929, 10053, 10054, 10060, 233, 64];

    private static readonly SqlRetryLogicBaseProvider OpenRetry = SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
    {
        NumberOfTries = 8,
        DeltaTime = TimeSpan.FromSeconds(2),
        MaxTimeInterval = TimeSpan.FromSeconds(20),
        TransientErrors = TransientOpenErrors,
    });

    public static SqlConnection Create(string connectionString) => new(connectionString) { RetryLogicProvider = OpenRetry };
}
