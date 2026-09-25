using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Drammers.Infrastructure.Health;

/// <summary>
/// Achtergrondverwerking (ADR-007): de heartbeat-job moet recent geslaagd zijn, op welke instantie dan ook.
/// Rapporteert <c>Degraded</c> (geen <c>Unhealthy</c>): de API zelf werkt dan nog.
/// </summary>
public sealed class WorkerHealthCheck(DrammersDbContext db, IClock clock) : IHealthCheck
{
    public static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromMinutes(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = await db.ScheduledJobs.AsNoTracking()
            .SingleOrDefaultAsync(j => j.Name == HeartbeatJob.JobName, cancellationToken);
        var data = new Dictionary<string, object>();
        var outboxBacklog = await db.Outbox.CountAsync(m => m.ProcessedAt == null, cancellationToken);
        data["outboxBacklog"] = outboxBacklog;

        if (state?.LastSucceededAt is not { } lastSucceeded)
        {
            return HealthCheckResult.Healthy("Heartbeat nog niet uitgevoerd", data);
        }

        data["lastHeartbeat"] = lastSucceeded;
        return clock.UtcNow.UtcDateTime - lastSucceeded > MaxHeartbeatAge
            ? HealthCheckResult.Degraded("Heartbeat te oud", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
