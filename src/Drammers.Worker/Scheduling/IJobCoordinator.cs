namespace Drammers.Worker.Scheduling;

/// <summary>
/// Bepaalt of deze instantie een job nu mag starten. De implementatie gebruikt <c>sp_getapplock</c> en de laatste
/// starttijd in de database, zodat bij scale-out (meerdere instanties) een job niet dubbel draait.
/// </summary>
public interface IJobCoordinator
{
    /// <summary><c>true</c> als deze instantie de job nu moet uitvoeren; de start is dan al vastgelegd.</summary>
    Task<bool> TryStartAsync(string jobName, TimeSpan interval, CancellationToken cancellationToken);

    Task CompleteAsync(string jobName, string? error, CancellationToken cancellationToken);
}
