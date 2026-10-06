using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Import.Sync;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <summary>
/// Nachtelijke ledensync (ADR-010, OQ-06): om 03:00 (Loil) één keer per dag, alleen als de feature flag
/// <c>members-sync</c> aan staat. De sync zelf loopt via de outbox. Draait alleen op dat moment, zodat een serverless
/// database de rest van de dag kan pauzeren.
/// </summary>
public sealed class MemberSyncScheduleJob(
    DrammersDbContext db, MemberSync sync, IClock clock, Microsoft.Extensions.Options.IOptions<EBoekhouden.EBoekhoudenOptions> eBoekhoudenOptions) : IRecurringJob
{
    public const string JobName = "member-sync-schedule";

    public static readonly TimeZoneInfo Loil = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Elke nacht om 03:00 in Loil.</summary>
    public static readonly Func<DateTimeOffset, DateTimeOffset> Schedule = JobSchedule.DailyAt(Loil, 3, 0);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var enabled = eBoekhoudenOptions.Value.Enabled
            && await db.FeatureFlags.AsNoTracking().AnyAsync(f => f.Key == MemberSyncSettings.ScheduleFlag && f.Enabled, cancellationToken);
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
            // Een handmatige run is bezig; die haalt de leden al op.
        }
    }
}
