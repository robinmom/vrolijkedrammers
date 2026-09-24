namespace Drammers.SharedKernel.Time;

/// <summary>
/// Abstractie van de systeemtijd, zodat tijdafhankelijke logica testbaar is.
/// Alle tijden zijn UTC; presentatie in Europe/Amsterdam gebeurt in de clients (docs/03 §7).
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
