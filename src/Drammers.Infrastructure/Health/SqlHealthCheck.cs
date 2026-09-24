using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Drammers.Infrastructure.Health;

/// <summary>Controleert of de database bereikbaar is met de managed identity (Entra-only, geen wachtwoord).</summary>
public sealed class SqlHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or Azure.Identity.AuthenticationFailedException)
        {
            return HealthCheckResult.Unhealthy("Database niet bereikbaar", ex);
        }
    }
}
