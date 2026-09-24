using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Drammers.Infrastructure.Health;

/// <summary>Controleert of Key Vault bereikbaar is en de managed identity secrets mag lezen.</summary>
public sealed class KeyVaultHealthCheck(SecretClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(cancellationToken).AsPages(pageSizeHint: 1))
            {
                break;
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is RequestFailedException or Azure.Identity.AuthenticationFailedException)
        {
            return HealthCheckResult.Unhealthy("Key Vault niet bereikbaar", ex);
        }
    }
}
