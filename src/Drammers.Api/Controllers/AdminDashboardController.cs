using Drammers.Api.Authorization;
using Drammers.Infrastructure;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Scheduling;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Drammers.Api.Controllers;

/// <summary>Basisdashboard (fase 4): kerncijfers en systeemstatus. Uitgebreid in latere fasen.</summary>
[ApiController]
[Route("api/v1/admin/dashboard")]
[RequirePermission(Permissions.ReportView)]
public sealed class AdminDashboardController(DrammersDbContext db, HealthCheckService health, IClock clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardResponse>(StatusCodes.Status200OK)]
    public async Task<DashboardResponse> Get(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var year = await db.CarnivalYears.AsNoTracking().Where(y => y.Active)
            .Select(y => new { y.Name, y.CarnivalStartDate }).SingleOrDefaultAsync(cancellationToken);
        var report = await health.CheckHealthAsync(c => c.Tags.Contains(DependencyInjection.ReadyTag), cancellationToken);
        var heartbeat = await db.ScheduledJobs.AsNoTracking().Where(j => j.Name == HeartbeatJob.JobName)
            .Select(j => j.LastSucceededAt).SingleOrDefaultAsync(cancellationToken);

        return new DashboardResponse(
            await db.Users.CountAsync(u => u.AccountStatus == AccountStatus.Active, cancellationToken),
            await db.Users.CountAsync(u => u.AccountStatus == AccountStatus.Blocked, cancellationToken),
            year?.Name,
            year is null ? null : year.CarnivalStartDate.DayNumber - today.DayNumber,
            await db.Outbox.CountAsync(m => m.ProcessedAt == null, cancellationToken),
            heartbeat,
            report.Status.ToString(),
            [.. report.Entries.Select(e => new HealthEntry(e.Key, e.Value.Status.ToString()))]);
    }
}

public sealed record DashboardResponse(
    int ActiveUsers,
    int BlockedUsers,
    string? CarnivalYear,
    int? DaysUntilCarnival,
    int OutboxBacklog,
    DateTime? LastHeartbeat,
    string SystemStatus,
    IReadOnlyList<HealthEntry> Checks);

public sealed record HealthEntry(string Name, string Status);
