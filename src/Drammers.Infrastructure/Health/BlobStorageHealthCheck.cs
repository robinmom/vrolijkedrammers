using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Drammers.Infrastructure.Health;

/// <summary>Controleert of Blob Storage bereikbaar is en de verwachte container bestaat.</summary>
public sealed class BlobStorageHealthCheck(BlobServiceClient client) : IHealthCheck
{
    public const string ProbeContainer = "dataprotection";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await client.GetBlobContainerClient(ProbeContainer).ExistsAsync(cancellationToken);
            return exists.Value
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Container '{ProbeContainer}' ontbreekt");
        }
        catch (Exception ex) when (ex is RequestFailedException or Azure.Identity.AuthenticationFailedException)
        {
            return HealthCheckResult.Unhealthy("Blob Storage niet bereikbaar", ex);
        }
    }
}
