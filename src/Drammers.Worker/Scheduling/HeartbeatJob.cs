using Microsoft.Extensions.Logging;

namespace Drammers.Worker.Scheduling;

/// <summary>
/// Heartbeat van de worker: elke minuut in het geheugen (<see cref="WorkerHeartbeat"/>) en in de logs. Bewust zonder
/// database, zodat een serverless database kan pauzeren; de health check leest de heartbeat van deze instantie.
/// </summary>
public sealed partial class HeartbeatJob(WorkerHeartbeat heartbeat, TimeProvider time, ILogger<HeartbeatJob> logger) : IRecurringJob
{
    public const string JobName = "heartbeat";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        heartbeat.Beat(time.GetUtcNow());
        LogHeartbeat(logger, Environment.MachineName);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Heartbeat vanaf instantie {Instance}")]
    private static partial void LogHeartbeat(ILogger logger, string instance);
}
