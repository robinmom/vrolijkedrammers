using Drammers.Infrastructure.Persistence;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Drammers.Infrastructure.Health;

/// <summary>
/// Achtergrondverwerking (ADR-007): de heartbeat van de worker op deze instantie moet recent zijn (in het geheugen, zodat
/// de worker de database niet elke minuut aanraakt). Rapporteert <c>Degraded</c> (geen <c>Unhealthy</c>): de API zelf
/// werkt dan nog.
/// </summary>
public sealed class WorkerHealthCheck(DrammersDbContext db, WorkerHeartbeat heartbeat, TimeProvider time) : IHealthCheck
{
    public static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromMinutes(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["outboxBacklog"] = await db.Outbox.CountAsync(m => m.ProcessedAt == null && m.Attempts < Worker.Outbox.OutboxProcessor.MaxAttempts, cancellationToken),
        };

        if (heartbeat.Last is not { } last)
        {
            return HealthCheckResult.Healthy("Heartbeat nog niet uitgevoerd", data);
        }

        data["lastHeartbeat"] = last;
        return time.GetUtcNow() - last > MaxHeartbeatAge
            ? HealthCheckResult.Degraded("Heartbeat te oud", data: data)
            : HealthCheckResult.Healthy(data: data);
    }
}
