using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Drammers.Infrastructure.Auditing;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.EBoekhouden;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Health;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Messaging;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Scheduling;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Messaging;
using Drammers.Worker;
using Drammers.Worker.Outbox;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Drammers.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Tag van de health checks die <c>/health/ready</c> uitvoert.</summary>
    public const string ReadyTag = "ready";

    public const string ConnectionStringName = "Drammers";

    /// <summary>
    /// Registreert database, Azure-clients, worker en readiness-checks. Alleen wat geconfigureerd is wordt
    /// geregistreerd, zodat lokaal en in tests zonder Azure gewerkt kan worden; in Azure zet Bicep alle waarden.
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
            services.AddDrammersDatabase(connectionString);
            healthChecks.Add(new HealthCheckRegistration(
                "sql", _ => new SqlHealthCheck(connectionString), HealthStatus.Unhealthy, [ReadyTag], timeout));
            healthChecks.AddCheck<WorkerHealthCheck>("worker", HealthStatus.Degraded, [ReadyTag], timeout);

            if (configuration.GetValue("Worker:Enabled", defaultValue: true))
            {
                services.AddDrammersWorker();
                services.AddRecurringJob<ContentPublisherJob>(ContentPublisherJob.JobName, ContentPublisherJob.Interval);
                services.AddRecurringJob<MemberSyncScheduleJob>(MemberSyncScheduleJob.JobName, MemberSyncScheduleJob.Interval);
            }
        }

        // Graph in de External ID-tenant (provisioning, blokkeren); zonder configuratie faalt elke aanroep duidelijk.
        var graph = configuration.GetSection(GraphOptions.SectionName);
        services.Configure<GraphOptions>(graph);
        if (graph.Get<GraphOptions>()?.IsConfigured == true)
        {
            services.AddSingleton<GraphCredentialProvider>();
            services.AddHttpClient<IEntraUserDirectory, GraphEntraUserDirectory>();
        }
        else
        {
            services.TryAddSingleton<IEntraUserDirectory, UnconfiguredEntraUserDirectory>();
        }

        // E-mail via Azure Communication Services met de managed identity; lokaal alleen een logregel.
        var email = configuration.GetSection(EmailOptions.SectionName);
        services.Configure<EmailOptions>(email);
        if (email.Get<EmailOptions>()?.IsConfigured == true)
        {
            services.AddSingleton<IEmailSender, AcsEmailSender>();
        }

        // e-Boekhouden (ADR-010): token uit Key Vault; leegmaken van leden alleen in Dev en Acc.
        services.Configure<EBoekhoudenOptions>(configuration.GetSection(EBoekhoudenOptions.SectionName));
        services.AddHttpClient<IEBoekhoudenClient, EBoekhoudenClient>(http => http.Timeout = TimeSpan.FromSeconds(60));
        services.Configure<MemberDataOptions>(o => o.AllowPurge = configuration["Auth:RequiredEnvironmentAccess"] is "dev" or "acc");

        if (options.KeyVaultUri is not null)
        {
            services.AddSingleton(new SecretClient(options.KeyVaultUri, credential));
            healthChecks.AddCheck<KeyVaultHealthCheck>("keyvault", HealthStatus.Unhealthy, [ReadyTag], timeout);
        }

        if (options.BlobEndpoint is not null)
        {
            services.AddSingleton(new BlobServiceClient(options.BlobEndpoint, credential));
            services.AddSingleton<IFileStore, BlobFileStore>();
            healthChecks.AddCheck<BlobStorageHealthCheck>("blob", HealthStatus.Unhealthy, [ReadyTag], timeout);
        }

        // Lokaal/tests: Azurite via connection string (met account-key; SAS dan als service-SAS).
        var blobConnectionString = configuration.GetConnectionString("Blob");
        if (options.BlobEndpoint is null && !string.IsNullOrWhiteSpace(blobConnectionString))
        {
            services.AddSingleton(new BlobServiceClient(blobConnectionString));
            services.AddSingleton<IFileStore, BlobFileStore>();
        }

        return services;
    }

    /// <summary>DbContext en de databasegebonden diensten; ook los bruikbaar in integratietests.</summary>
    public static IServiceCollection AddDrammersDatabase(this IServiceCollection services, string connectionString)
    {
        services.AddMemoryCache();
        services.TryAddScoped<ICurrentActor, SystemActor>();
        services.AddScoped<AuditableInterceptor>();
        services.AddDbContext<DrammersDbContext>((provider, db) => db
            .UseSqlServer(connectionString)
            .AddInterceptors(provider.GetRequiredService<AuditableInterceptor>()));

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddScoped<IOutboxStore, SqlOutboxStore>();
        services.AddScoped<IJobCoordinator, SqlJobCoordinator>();
        services.AddScoped<AppConfigReader>();
        services.AddScoped<IUserAccessService, UserAccessService>();
        services.AddScoped<ILoginRecorder, LoginRecorder>();
        services.AddScoped<AccountAdministration>();
        services.AddScoped<ConfigurationAdministration>();
        services.AddScoped<ContentAdministration>();
        services.AddScoped<ContentFiles>();
        services.AddScoped<IOutboxMessageHandler, PhotoProcessingHandler>();
        services.AddScoped<MemberSync>();
        services.AddScoped<MemberSyncSettings>();
        services.AddScoped<MemberAdministration>();
        services.AddScoped<GroupAdministration>();
        services.AddScoped<IOutboxMessageHandler, MemberSyncHandler>();
        services.AddScoped<MemberAccounts>();
        services.AddScoped<IOutboxMessageHandler, MemberAccountProvisioningHandler>();
        services.AddScoped<MyAccount>();
        services.TryAddSingleton<IEmailSender, LoggingEmailSender>();
        services.TryAddSingleton<IEntraUserDirectory, UnconfiguredEntraUserDirectory>();
        services.TryAddSingleton<IEBoekhoudenClient, UnconfiguredEBoekhoudenClient>();
        services.TryAddSingleton<IMalwareScanner, NoMalwareScanner>();
        services.TryAddSingleton<IFileStore, UnconfiguredFileStore>();
        return services;
    }
}
