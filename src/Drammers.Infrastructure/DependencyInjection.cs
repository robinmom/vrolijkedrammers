using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Drammers.Infrastructure.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Drammers.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Tag van de health checks die <c>/health/ready</c> uitvoert.</summary>
    public const string ReadyTag = "ready";

    public const string ConnectionStringName = "Drammers";

    /// <summary>
    /// Registreert de Azure-clients en de readiness-checks. Alleen wat geconfigureerd is wordt geregistreerd,
    /// zodat lokaal en in tests zonder Azure gewerkt kan worden; in Azure zet Bicep alle waarden.
    /// </summary>
    public static IServiceCollection AddDrammersInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(AzureOptions.SectionName).Get<AzureOptions>() ?? new AzureOptions();
        var healthChecks = services.AddHealthChecks();
        var timeout = TimeSpan.FromSeconds(30);

        // In Azure alleen de managed identity; lokaal de ingelogde ontwikkelaar (az login / Visual Studio).
        TokenCredential credential = environment.IsDevelopment()
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true })
            : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
        services.AddSingleton(credential);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            healthChecks.Add(new HealthCheckRegistration(
                "sql", _ => new SqlHealthCheck(connectionString), HealthStatus.Unhealthy, [ReadyTag], timeout));
        }

        if (options.KeyVaultUri is not null)
        {
            services.AddSingleton(new SecretClient(options.KeyVaultUri, credential));
            healthChecks.AddCheck<KeyVaultHealthCheck>("keyvault", HealthStatus.Unhealthy, [ReadyTag], timeout);
        }

        if (options.BlobEndpoint is not null)
        {
            services.AddSingleton(new BlobServiceClient(options.BlobEndpoint, credential));
            healthChecks.AddCheck<BlobStorageHealthCheck>("blob", HealthStatus.Unhealthy, [ReadyTag], timeout);
        }

        return services;
    }
}
