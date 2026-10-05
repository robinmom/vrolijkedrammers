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
using Drammers.Infrastructure.Notifications;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Scheduling;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Messaging;
using Drammers.Worker;
using Drammers.Worker.Outbox;
using Drammers.Worker.Scheduling;
using Microsoft.AspNetCore.DataProtection;
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
                // Alleen op vaste momenten (geen minuutlijkse controles): de serverless database mag de rest pauzeren.
                services.AddScheduledJob<ContentPublisherJob>(ContentPublisherJob.JobName, JobSchedule.DailyAt(MemberSyncScheduleJob.Loil, 3, 15), TimeSpan.FromHours(20));
                services.AddScheduledJob<MemberSyncScheduleJob>(MemberSyncScheduleJob.JobName, MemberSyncScheduleJob.Schedule, TimeSpan.FromHours(20));
                services.AddScheduledJob<DataRetentionJob>(DataRetentionJob.JobName, JobSchedule.DailyAt(MemberSyncScheduleJob.Loil, 3, 30), TimeSpan.FromHours(20));
                services.AddScheduledJob<ParadeDeadlineReminderJob>(ParadeDeadlineReminderJob.JobName, JobSchedule.DailyAt(MemberSyncScheduleJob.Loil, 18, 0), TimeSpan.FromHours(20));
                services.AddScheduledJob<Sales.SaleExpiryJob>(Sales.SaleExpiryJob.JobName, JobSchedule.DailyAt(MemberSyncScheduleJob.Loil, 3, 45), TimeSpan.FromHours(20));
            }
        }

        // Graph in de External ID-tenant (provisioning, blokkeren); zonder configuratie faalt elke aanroep duidelijk.
        var graph = configuration.GetSection(GraphOptions.SectionName);
        services.Configure<GraphOptions>(graph);
        services.Configure<TestAccessOptions>(o =>
        {
            o.Environment = configuration["Auth:RequiredEnvironmentAccess"];
            o.Grant = configuration["Graph:TestAccessEnvironments"] is { Length: > 0 } grant ? grant : o.Grant;
        });
        if (graph.Get<GraphOptions>()?.IsConfigured == true)
        {
            services.AddSingleton<GraphCredentialProvider>();
            services.AddHttpClient<IEntraUserDirectory, GraphEntraUserDirectory>();
        }
        else
        {
            services.TryAddSingleton<IEntraUserDirectory, UnconfiguredEntraUserDirectory>();
        }

        // Data Protection (push-tokens): in Azure een gedeelde sleutelring in Blob, beschermd door een Key Vault-sleutel;
        // lokaal en in tests de standaardopslag van ASP.NET.
        var dataProtection = services.AddDataProtection().SetApplicationName("drammers");
        if (options.BlobEndpoint is not null && options.DataProtectionKeyUri is not null)
        {
            dataProtection
                .PersistKeysToAzureBlobStorage(new Uri(options.BlobEndpoint, "dataprotection/keys.xml"), credential)
                .ProtectKeysWithAzureKeyVault(options.DataProtectionKeyUri, credential);
        }

        // Push (ADR-009): Expo met het access token uit Key Vault, anders gesimuleerd.
        var push = configuration.GetSection(PushOptions.SectionName);
        services.Configure<PushOptions>(push);
        if ((push.Get<PushOptions>() ?? new PushOptions()).UseExpo)
        {
            services.AddHttpClient<IPushSender, ExpoPushSender>(http => http.Timeout = TimeSpan.FromSeconds(30));
        }

        // E-mail via Azure Communication Services met de managed identity; lokaal alleen een logregel.
        var email = configuration.GetSection(EmailOptions.SectionName);
        services.Configure<EmailOptions>(email);
        // Logo bovenaan de e-mails: standaard dat van de website op dezelfde host als de links in de mails.
        services.PostConfigure<EmailOptions>(o => o.LogoUrl ??= configuration["Sales:PublicBaseUrl"] is { Length: > 0 } baseUrl
            ? $"{baseUrl.TrimEnd('/')}/_content/Drammers.Website/img/logo.png"
            : null);
        if (email.Get<EmailOptions>()?.IsConfigured == true)
        {
            services.AddSingleton<IEmailSender, AcsEmailSender>();
        }

        // Contactformulier van de website (fase 21i); Turnstile staat aan zodra beide sleutels zijn gezet.
        services.Configure<Contact.ContactOptions>(configuration.GetSection(Contact.ContactOptions.SectionName));
        services.Configure<Contact.TurnstileOptions>(configuration.GetSection(Contact.TurnstileOptions.SectionName));
        services.AddHttpClient<Contact.ITurnstileVerifier, Contact.TurnstileVerifier>(http => http.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<Contact.ContactForm>();

        // Mollie (fase 19): API-sleutel uit Key Vault (secret mollie-api-key), test-sleutel buiten productie.
        services.Configure<Payments.MollieOptions>(configuration.GetSection(Payments.MollieOptions.SectionName));
        services.Configure<Sales.SalesOptions>(configuration.GetSection(Sales.SalesOptions.SectionName));
        // Fase 27a: mailings; foto's en afmeldlink op hetzelfde openbare adres als de links in de andere mails.
        services.Configure<Mailings.MailingOptions>(configuration.GetSection(Mailings.MailingOptions.SectionName));
        services.PostConfigure<Mailings.MailingOptions>(o =>
        {
            o.PublicBaseUrl ??= configuration["Sales:PublicBaseUrl"] is { Length: > 0 } baseUrl ? baseUrl : null;
            o.HasCustomDomain = !string.IsNullOrWhiteSpace(configuration["Email:CustomSenderDomain"]);
        });
        services.AddMemoryCache();
        services.AddHttpClient<Payments.IMollieClient, Payments.MollieClient>(http => http.Timeout = TimeSpan.FromSeconds(30));

        // e-Boekhouden (ADR-010): token uit Key Vault; leegmaken van leden alleen in Dev en Acc.
        services.Configure<EBoekhoudenOptions>(configuration.GetSection(EBoekhoudenOptions.SectionName));
        services.Configure<ParadeManagement.ResultsOptions>(configuration.GetSection(ParadeManagement.ResultsOptions.SectionName));
        services.Configure<Content.Import.WordPressImportOptions>(configuration.GetSection(Content.Import.WordPressImportOptions.SectionName));
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
        services.TryAddSingleton<OutboxSignal>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OutboxSignalInterceptor>();
        services.AddScoped<ParadeChangeContext>();
        services.AddScoped<ParadeHistoryInterceptor>();
        services.AddDbContext<DrammersDbContext>((provider, db) => db
            // Eigen verbinding met herhaalpogingen bij het openen (serverless database die opstart); EF sluit hem.
            .UseSqlServer(SqlConnectionFactory.Create(connectionString), contextOwnsConnection: true)
            .AddInterceptors(
                provider.GetRequiredService<AuditableInterceptor>(),
                provider.GetRequiredService<OutboxSignalInterceptor>(),
                provider.GetRequiredService<ParadeHistoryInterceptor>()));

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddScoped<IOutboxStore, SqlOutboxStore>();
        services.AddScoped<IJobCoordinator, SqlJobCoordinator>();
        services.AddScoped<AppConfigReader>();
        services.AddScoped<IUserAccessService, UserAccessService>();
        services.AddScoped<ILoginRecorder, LoginRecorder>();
        services.AddScoped<AccountAdministration>();
        services.AddScoped<TestAccessAdministration>();
        services.AddScoped<ConfigurationAdministration>();
        services.AddScoped<ContentAdministration>();
        services.AddScoped<CarnivalSeasons>();
        services.AddScoped<WebsiteAdministration>();
        // Overzetten van de oude WordPress-site (fase 21e): in porties via de outbox.
        services.AddOptions<Content.Import.WordPressImportOptions>();
        services.AddHttpClient<Content.Import.WordPressSource>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(60);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DeVrolijkeDrammers-Import/1.0");
        });
        services.AddScoped<Content.Import.WebsiteImporter>();
        services.AddScoped<IOutboxMessageHandler, Content.Import.WebsiteImportHandler>();
        services.AddScoped<ContentFiles>();
        services.AddScoped<IOutboxMessageHandler, PhotoProcessingHandler>();
        services.TryAddScoped<ContentPublisherJob>();
        services.AddScoped<IOutboxMessageHandler, ContentPublishDueHandler>();
        services.TryAddSingleton<WorkerHeartbeat>();
        services.AddScoped<MemberSync>();
        services.AddScoped<MemberSyncSettings>();
        services.AddScoped<MemberAdministration>();
        services.AddScoped<Jubilees>();
        services.AddScoped<JubileeInvitations>();
        services.AddScoped<Contributions>();
        services.AddScoped<MemberSplits>();
        services.AddScoped<MemberRequests>();
        services.AddScoped<SepaCollections>();
        services.AddSingleton<MemberIbanProtector>();
        services.AddScoped<IOutboxMessageHandler, JubileeInvitationMailHandler>();
        // Fase 27a: mailings (nieuwsbrief, uitnodigingen).
        services.AddScoped<Mailings.MailingService>();
        // Fase 27b: adverteerders.
        services.AddScoped<Advertisers.AdvertiserAdministration>();
        services.AddSingleton<Advertisers.AdvertiserIbanProtector>();
        services.AddScoped<Advertisers.Invoices.AdvertiserInvoices>();
        services.AddScoped<IOutboxMessageHandler, Advertisers.Invoices.AdvertiserInvoiceMailHandler>();
        services.AddSingleton<Mailings.MailingUnsubscribeTokens>();
        services.AddScoped<IOutboxMessageHandler, Mailings.MailingRecipientMailHandler>();
        services.AddScoped<GroupAdministration>();
        services.AddScoped<Guardians>();
        services.AddScoped<ParadeManagement.ParadeExchange>();
        services.AddScoped<ParadeManagement.ParadeArrivals>();
        services.AddScoped<Dansgarde>();
        services.AddScoped<IOutboxMessageHandler, MemberSyncHandler>();
        services.AddScoped<MemberAccounts>();
        services.AddScoped<IOutboxMessageHandler, MemberAccountProvisioningHandler>();
        services.AddScoped<IOutboxMessageHandler, MemberAccountReminderHandler>();
        services.AddScoped<MyAccount>();
        services.AddScoped<AccountLinker>();
        services.AddScoped<AccountLifecycle>();
        services.TryAddScoped<DataRetentionJob>();
        services.AddScoped<IOutboxMessageHandler, EntraAccountStateHandler>();
        services.AddScoped<MembershipApplications>();
        services.AddDataProtection();
        services.AddSingleton<PushTokenProtector>();
        services.AddScoped<NotificationAudienceResolver>();
        services.AddScoped<NotificationAdministration>();
        services.AddScoped<Modules.Notification.Notifications.INotificationService>(sp => sp.GetRequiredService<NotificationAdministration>());
        services.AddScoped<MyNotifications>();
        services.AddScoped<IOutboxMessageHandler, NotificationDispatchHandler>();
        services.AddScoped<IOutboxMessageHandler, NotificationReceiptsHandler>();
        services.TryAddScoped<IPushSender, SimulatedPushSender>();
        services.AddScoped<ParadeAdministration>();
        services.AddScoped<ParadeJury>();
        services.AddScoped<ParadeJudging>();
        services.AddScoped<ParadeResults>();
        services.AddScoped<ParadeRegistrations>();
        services.AddScoped<IOutboxMessageHandler, ParadeSubmittedMailHandler>();
        services.AddScoped<ParadeReview>();
        services.AddScoped<ParadeLineup>();
        services.AddScoped<ParadeComposition>();
        services.AddScoped<Ticketing.TicketSigningKeys>();
        services.AddScoped<Ticketing.AccessWindows>();
        services.AddScoped<Ticketing.MemberTickets>();
        services.AddScoped<Ticketing.TicketValidation>();
        services.AddScoped<Ticketing.TicketAdministration>();
        services.AddScoped<Ticketing.DoorAccess>();
        services.AddScoped<Ticketing.AccessStatistics>();
        services.AddScoped<Sales.TicketSales>();
        services.AddScoped<Sales.SaleAdministration>();
        services.AddScoped<Sales.TokenCollection>();
        services.TryAddScoped<Sales.SaleExpiryJob>();
        services.AddScoped<ParadePublicRegistrations>();
        services.AddScoped<IOutboxMessageHandler, ParadeStatusMailHandler>();
        services.TryAddScoped<ParadeDeadlineReminderJob>();
        services.AddScoped<IOutboxMessageHandler, MembershipProvisioningHandler>();
        services.TryAddSingleton<IEmailSender, LoggingEmailSender>();
        services.TryAddSingleton<IEntraUserDirectory, UnconfiguredEntraUserDirectory>();
        services.TryAddSingleton<IEBoekhoudenClient, UnconfiguredEBoekhoudenClient>();
        services.TryAddScoped<IEBoekhoudenWriter>(sp => EBoekhoudenWriterSelector.Select(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EBoekhoudenOptions>>(), sp.GetRequiredService<IEBoekhoudenClient>()));
        services.TryAddSingleton<IMalwareScanner, NoMalwareScanner>();
        services.TryAddSingleton<IFileStore, UnconfiguredFileStore>();
        return services;
    }
}
