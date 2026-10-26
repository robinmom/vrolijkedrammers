namespace Drammers.Modules.Ticketing.Sales;

/// <summary>
/// De instelling van de tafelindeling per pronkzittingavond (fase 19t): hoeveel tafels er per rij staan. Er zijn altijd
/// vier rijen en acht plaatsen per tafel, en het podium staat bovenaan. Eén rij per avond (<see cref="ProductId"/>).
/// </summary>
public sealed class PronkzittingSeatingLayout
{
    /// <summary>De avond: een <see cref="SaleProduct"/> met <see cref="SaleProductKind.Pronkzitting"/>. Tevens de sleutel.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Het aantal tafels per rij (instelbaar door het bestuur); samen met de vaste vier rijen de capaciteit van de zaal.</summary>
    public int TablesPerRow { get; set; } = 6;

    public DateTime? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// De plaats van één gezelschap (een groep uit e-Boekhouden of een losse bestelling) in de tafelindeling van een avond
/// (fase 19t). De indeling wordt per gezelschap bewaard; een gezelschap blijft in dezelfde rij en kan over meerdere tafels
/// in die rij lopen. Gezelschappen zonder bewaarde plaats worden bij het tonen automatisch (gebalanceerd) ingedeeld,
/// totdat het bestuur ze versleept.
/// </summary>
public sealed class PronkzittingSeat
{
    public Guid Id { get; set; }

    /// <summary>De avond (<see cref="SaleProduct"/>).</summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// De sleutel van het gezelschap, stabiel over herladen: <c>G:</c> gevolgd door de groepsnaam voor een groep, of
    /// <c>O:</c> gevolgd door de id van de losse bestelling.
    /// </summary>
    public required string PartyKey { get; set; }

    /// <summary>De rij (0..3); rij 0 staat het dichtst bij het podium.</summary>
    public byte RowIndex { get; set; }

    /// <summary>De volgorde binnen de rij, van links naar rechts.</summary>
    public int Position { get; set; }
}
