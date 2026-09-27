namespace Drammers.Worker.Scheduling;

/// <summary>
/// Tijdgestuurde job die over alle instanties heen hooguit één keer per interval draait (ADR-007).
/// Registreren met <see cref="WorkerServiceCollectionExtensions.AddRecurringJob{TJob}"/>.
/// </summary>
public interface IRecurringJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Naam (sleutel voor coördinatie en status), interval en implementatie van een job.
/// </summary>
/// <param name="Name">Sleutel voor coördinatie en status.</param>
/// <param name="Interval">Tijd tussen twee runs; bij <paramref name="NextRun"/> het venster waarin de job op andere instanties niet opnieuw start.</param>
/// <param name="NextRun">Vast moment in plaats van een interval, bijv. elke nacht om 03:00 (ADR-007); de eerste run wacht daarop.</param>
/// <param name="JobType">Implementatie (<see cref="IRecurringJob"/>), per run uit een nieuwe scope.</param>
/// <param name="Coordinated">
/// Via de database over instanties heen coördineren. Uit voor jobs die alleen lokaal iets doen (heartbeat): dan raakt de
/// job de database niet, zodat een serverless database kan pauzeren.
/// </param>
public sealed record RecurringJobRegistration(
    string Name, TimeSpan Interval, Type JobType, Func<DateTimeOffset, DateTimeOffset>? NextRun = null, bool Coordinated = true);

/// <summary>Tijdstippen voor <see cref="RecurringJobRegistration.NextRun"/>.</summary>
public static class JobSchedule
{
    /// <summary>Elke dag op dit tijdstip in de gegeven tijdzone (zomer- en wintertijd inbegrepen).</summary>
    public static Func<DateTimeOffset, DateTimeOffset> DailyAt(TimeZoneInfo zone, int hour, int minute) => now =>
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var candidate = new DateTime(local.Year, local.Month, local.Day, hour, minute, 0, DateTimeKind.Unspecified);
        if (candidate <= local.DateTime)
        {
            candidate = candidate.AddDays(1);
        }

        return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
    };
}

/// <summary>Laatste heartbeat van de worker op deze instantie (in het geheugen, dus zonder database).</summary>
public sealed class WorkerHeartbeat
{
    public DateTimeOffset? Last { get; private set; }

    public void Beat(DateTimeOffset now) => Last = now;
}
