using Drammers.Worker.Outbox;
using Drammers.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.Worker;

public static class WorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registreert de hosted services van de worker (B-01: in hetzelfde proces als de API). De opslag
    /// (<see cref="IJobCoordinator"/>, <see cref="IOutboxStore"/>) komt uit de infrastructuurlaag.
    /// </summary>
    public static IServiceCollection AddDrammersWorker(this IServiceCollection services)
    {
        services.AddRecurringJob<HeartbeatJob>(HeartbeatJob.JobName, HeartbeatJob.Interval);
        services.AddHostedService<RecurringJobScheduler>();
        services.AddHostedService<OutboxProcessor>();
        return services;
    }

    /// <summary>Registreert een job; de scheduler maakt per uitvoering een nieuwe scope en instantie.</summary>
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services, string name, TimeSpan interval)
        where TJob : class, IRecurringJob
    {
        services.AddScoped<TJob>();
        services.AddSingleton(new RecurringJobRegistration(name, interval, typeof(TJob)));
        return services;
    }
}
