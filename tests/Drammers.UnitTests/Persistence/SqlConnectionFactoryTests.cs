using Drammers.Infrastructure.Persistence;

namespace Drammers.UnitTests.Persistence;

public class SqlConnectionFactoryTests
{
    [Fact]
    public void Openen_wordt_herhaald_bij_een_database_die_opstart()
    {
        using var connection = SqlConnectionFactory.Create("Server=tcp:localhost,1433;Database=x;Encrypt=True");

        Assert.NotNull(connection.RetryLogicProvider);
        Assert.Equal(8, connection.RetryLogicProvider.RetryLogic.NumberOfTries);
        // 40613 = "Database is not currently available" (serverless database die hervat).
        Assert.Contains(40613, SqlConnectionFactory.TransientOpenErrors);
    }
}
