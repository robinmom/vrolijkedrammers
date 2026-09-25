using System.Data;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Scheduling;

/// <summary>
/// Coördineert jobs over instanties heen: <c>sp_getapplock</c> (transactie-eigenaar) serialiseert de beslissing,
/// en <c>config.ScheduledJob</c> onthoudt wanneer de job voor het laatst startte (ADR-007).
/// </summary>
internal sealed class SqlJobCoordinator(DrammersDbContext db, IClock clock) : IJobCoordinator
{
    /// <summary>Marge voor timers die net iets te vroeg afgaan.</summary>
    private const double IntervalTolerance = 0.9;

    public async Task<bool> TryStartAsync(string jobName, TimeSpan interval, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var resource = $"job:{jobName}";
        var lockResult = (await db.Database.SqlQuery<int>(
            $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
            SELECT @result AS [Value];
            """).ToListAsync(cancellationToken)).Single();
        if (lockResult < 0)
        {
            return false;
        }

        var now = clock.UtcNow.UtcDateTime;
        var state = await db.ScheduledJobs.SingleOrDefaultAsync(j => j.Name == jobName, cancellationToken);
        if (state?.LastStartedAt is { } lastStarted && lastStarted > now - (interval * IntervalTolerance))
        {
            return false;
        }

        if (state is null)
        {
            state = new ScheduledJobState { Name = jobName };
            db.ScheduledJobs.Add(state);
        }

        state.LastStartedAt = now;
        state.LastInstance = Environment.MachineName;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task CompleteAsync(string jobName, string? error, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        await db.ScheduledJobs.Where(j => j.Name == jobName).ExecuteUpdateAsync(
            s => s.SetProperty(j => j.LastCompletedAt, now)
                .SetProperty(j => j.LastSucceededAt, j => error == null ? now : j.LastSucceededAt)
                .SetProperty(j => j.LastError, error),
            cancellationToken);
    }
}
