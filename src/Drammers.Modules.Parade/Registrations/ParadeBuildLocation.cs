namespace Drammers.Modules.Parade.Registrations;

/// <summary>
/// Onthouden bouwlocatie van een groepsverantwoordelijke (fase 11): staat niet in e-Boekhouden, wordt één keer ingevuld
/// en volgend jaar aangeboden ("zelfde locatie"). De gebruiker kan oude locaties verwijderen.
/// </summary>
public sealed class ParadeBuildLocation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Address Address { get; set; } = new();

    public DateTime CreatedAt { get; set; }

    public DateTime LastUsedAt { get; set; }
}
