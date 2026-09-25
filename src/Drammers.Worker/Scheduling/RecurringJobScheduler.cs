using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Drammers.Worker.Scheduling;

/// <summary>
/// Draait alle <see cref="IRecurringJob"/>s met een eigen <see cref="PeriodicTimer"/> per job. Voor elke tick vraagt
/// de scheduler de <see cref="IJobCoordinator"/> of deze instantie mag starten; zo draait een job bij meerdere
/// instanties niet dubbel. Stoppen gebeurt netjes via het stop-token van de host.
/// </summary>
public sealed partial class RecurringJobScheduler(
    IServiceScopeFactory scopeFactory,
    IEnumerable<RecurringJobRegistration> jobs,
    ILogger<RecurringJobScheduler> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(jobs.Select(job => RunJobLoopAsync(job, stoppingToken)));

    private async Task RunJobLoopAsync(RecurringJobRegistration job, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(job.Interval);
        do
        {
            await RunOnceAsync(job, stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Eén poging om de job uit te voeren; publiek voor tests.</summary>
    public async Task<bool> RunOnceAsync(RecurringJobRegistration registration, CancellationToken stoppingToken)
    {
        var (jobName, interval, jobType) = registration;
        if (stoppingToken.IsCancellationRequested)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IJobCoordinator>();
        var job = (IRecurringJob)scope.ServiceProvider.GetRequiredService(jobType);

        try
        {
            if (!await coordinator.TryStartAsync(jobName, interval, stoppingToken))
            {
                LogSkipped(logger, jobName);
                return false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCoordinatorFailed(logger, ex, jobName);
            return false;
        }

        string? error = null;
        try
        {
            await job.ExecuteAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            error = "Afgebroken bij het stoppen van de applicatie";
        }
        catch (Exception ex)
        {
            error = ex.Message;
            LogJobFailed(logger, ex, jobName);
        }

        await coordinator.CompleteAsync(jobName, error, CancellationToken.None);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Job {JobName} overgeslagen: draait al of is recent uitgevoerd op een andere instantie")]
    private static partial void LogSkipped(ILogger logger, string jobName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Coördinatie van job {JobName} mislukt; volgende tick opnieuw")]
    private static partial void LogCoordinatorFailed(ILogger logger, Exception exception, string jobName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobName} is mislukt")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, string jobName);
}
