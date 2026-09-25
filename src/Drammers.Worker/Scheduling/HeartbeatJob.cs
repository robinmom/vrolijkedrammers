using Microsoft.Extensions.Logging;

namespace Drammers.Worker.Scheduling;

/// <summary>Voorbeeldjob: logt elke minuut een heartbeat, zichtbaar in de logs en de health check.</summary>
public sealed partial class HeartbeatJob(ILogger<HeartbeatJob> logger) : IRecurringJob
{
    public const string JobName = "heartbeat";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        LogHeartbeat(logger, Environment.MachineName);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Heartbeat vanaf instantie {Instance}")]
    private static partial void LogHeartbeat(ILogger logger, string instance);
}
