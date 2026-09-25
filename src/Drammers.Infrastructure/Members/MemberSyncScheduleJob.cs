using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Import.Sync;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <summary>
/// Nachtelijke ledensync (ADR-010, OQ-06): vanaf 03:00 (Loil) één keer per dag, alleen als de feature flag
/// <c>members-sync</c> aan staat. Controleert elk kwartier; de sync zelf loopt via de outbox.
/// </summary>
public sealed class MemberSyncScheduleJob(DrammersDbContext db, MemberSync sync, IClock clock) : IRecurringJob
{
    public const string JobName = "member-sync-schedule";

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private static readonly TimeZoneInfo Loil = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var enabled = await db.FeatureFlags.AsNoTracking().AnyAsync(f => f.Key == MemberSyncSettings.ScheduleFlag && f.Enabled, cancellationToken);
        if (!enabled)
        {
            return;
        }

        var now = clock.UtcNow;
        var local = TimeZoneInfo.ConvertTime(now, Loil);
        if (local.Hour < 3)
        {
            return;
        }

        // Vandaag 03:00 in Loil, als UTC-tijdstip.
        var todayAtThree = new DateTimeOffset(local.Year, local.Month, local.Day, 3, 0, 0, local.Offset).UtcDateTime;
        var alreadyToday = await db.SyncJobs.AnyAsync(j => j.Trigger == SyncTrigger.Scheduled && j.RequestedAt >= todayAtThree, cancellationToken);
        if (alreadyToday)
        {
            return;
        }

        try
        {
            await sync.RequestAsync(dryRun: false, SyncTrigger.Scheduled, requestedBy: null, cancellationToken);
        }
        catch (DomainException ex) when (ex.Code == ErrorCodes.SyncAlreadyRunning)
        {
            // Een handmatige run is bezig; het volgende kwartier opnieuw proberen.
        }
    }
}
