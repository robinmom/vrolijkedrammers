namespace Drammers.Modules.Ticketing.Sales;

/// <summary>Wat er te koop is (fase 19).</summary>
public enum SaleProductKind
{
    /// <summary>Eén avond van de pronkzitting; leden bestellen gratis voor hun groep, niet-leden betalen.</summary>
    Pronkzitting,

    /// <summary>Dagkaart carnaval voor gasten; leden hebben Mijn QR.</summary>
    DayTicket,

    /// <summary>Kaart voor een activiteit uit de agenda.</summary>
    EventTicket,

    /// <summary>Consumptiemunten: alleen leden, persoonsgebonden, afhalen bij de kassa.</summary>
    Tokens,
}

/// <summary>
/// Een product in de kaartverkoop (fase 19): een pronkzittingavond, een dagkaart per carnavalsdag, kaarten voor een
/// activiteit of munten. De prijs komt altijd van de server; <see cref="Capacity"/> leeg is onbeperkt.
/// </summary>
public sealed class SaleProduct
{
    public Guid Id { get; set; }

    public int CarnivalYearId { get; set; }

    public SaleProductKind Kind { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>De activiteit in de agenda (kaarten per activiteit; optioneel bij de pronkzitting).</summary>
    public Guid? EventId { get; set; }

    /// <summary>De dag waarvoor de kaart geldt (pronkzittingavond, carnavalsdag); leeg bij munten.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Prijs per kaart of per munt in centen.</summary>
    public int PriceCents { get; set; }

    public int? Capacity { get; set; }

    /// <summary>Hoeveel er maximaal in één bestelling mag (losse kaarten of munten).</summary>
    public int MaxPerOrder { get; set; } = 10;

    public DateTime? SaleOpensAt { get; set; }

    public DateTime? SaleClosesAt { get; set; }

    /// <summary>Aan = te koop (binnen de verkoopperiode).</summary>
    public bool OnSale { get; set; }

    public int SortOrder { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    /// <summary>Leden bestellen voor hun groep gratis (in de contributie).</summary>
    public bool GroupOrders => Kind == SaleProductKind.Pronkzitting;

    /// <summary>Alleen leden mogen kopen (munten zijn persoonsgebonden).</summary>
    public bool MembersOnly => Kind == SaleProductKind.Tokens;

    /// <summary>Dagkaarten zijn voor gasten: leden hebben Mijn QR.</summary>
    public bool GuestsOnly => Kind == SaleProductKind.DayTicket;
}
