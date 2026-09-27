using Drammers.Worker.Outbox;
using Drammers.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Drammers.Worker;

public static class WorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registreert de hosted services van de worker (B-01: in hetzelfde proces als de API). De opslag
    /// (<see cref="IJobCoordinator"/>, <see cref="IOutboxStore"/>) komt uit de infrastructuurlaag.
    /// </summary>
    public static IServiceCollection AddDrammersWorker(this IServiceCollection services)
    {
        services.TryAddSingleton<WorkerHeartbeat>();
        services.TryAddSingleton<OutboxSignal>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<HeartbeatJob>();
        services.AddSingleton(new RecurringJobRegistration(HeartbeatJob.JobName, HeartbeatJob.Interval, typeof(HeartbeatJob), Coordinated: false));
        services.AddHostedService<RecurringJobScheduler>();
        services.AddHostedService<OutboxProcessor>();
        return services;
    }

    /// <summary>Job op een vast moment (bijv. <see cref="JobSchedule.DailyAt"/>); <paramref name="window"/> voorkomt een dubbele run.</summary>
    public static IServiceCollection AddScheduledJob<TJob>(
        this IServiceCollection services, string name, Func<DateTimeOffset, DateTimeOffset> nextRun, TimeSpan window)
        where TJob : class, IRecurringJob
    {
        services.AddScoped<TJob>();
        services.AddSingleton(new RecurringJobRegistration(name, window, typeof(TJob), nextRun));
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
