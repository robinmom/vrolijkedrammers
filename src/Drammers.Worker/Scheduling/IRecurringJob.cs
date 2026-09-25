namespace Drammers.Worker.Scheduling;

/// <summary>
/// Tijdgestuurde job die over alle instanties heen hooguit één keer per interval draait (ADR-007).
/// Registreren met <see cref="WorkerServiceCollectionExtensions.AddRecurringJob{TJob}"/>.
/// </summary>
public interface IRecurringJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>Naam (sleutel voor coördinatie en status), interval en implementatie van een job.</summary>
public sealed record RecurringJobRegistration(string Name, TimeSpan Interval, Type JobType);
