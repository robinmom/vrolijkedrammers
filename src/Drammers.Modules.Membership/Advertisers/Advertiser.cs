using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Membership.Advertisers;

/// <summary>Soort bijdrage (kolom A/V/G in het Excel-overzicht).</summary>
public enum AdvertiserKind
{
    /// <summary>A: advertentie in de Drammerskrant.</summary>
    Advertisement,

    /// <summary>V: vrije gift.</summary>
    FreeGift,

    /// <summary>G: gift.</summary>
    Gift,
}

/// <summary>Hoe de adverteerder betaalt (kolom M/C/R/B).</summary>
public enum AdvertiserPayment
{
    /// <summary>M: SEPA-machtiging (incasso).</summary>
    Mandate,

    /// <summary>C: contant, bij de collectant.</summary>
    Cash,

    /// <summary>R: rekening; de adverteerder krijgt een factuur en maakt zelf over (alleen als aanduiding).</summary>
    Invoice,
}

/// <summary>Stand van een adverteerder in de campagne van een jaar.</summary>
public enum AdvertiserYearStatus
{
    /// <summary>De collectant moet nog langs.</summary>
    Open,

    /// <summary>Opgehaald: de adverteerder doet dit jaar weer mee.</summary>
    Collected,

    /// <summary>Stopt: doet dit jaar niet mee.</summary>
    Stopped,
}

/// <summary>
/// Adverteerder of gever (fase 27b), overgenomen uit "Advertentie overzicht" (Excel). Elke adverteerder heeft een
/// collectant uit het kader die langsgaat; per jaar staat de bijdrage en de stand in <see cref="AdvertiserYear"/>.
/// De IBAN staat versleuteld opgeslagen, zoals bij leden.
/// </summary>
public sealed class Advertiser : IAuditable
{
    public Guid Id { get; set; }

    /// <summary>Nummer uit het overzicht (kolom NR.), uniek.</summary>
    public int Number { get; set; }

    public required string CompanyName { get; set; }

    public string? ContactName { get; set; }

    public string? Phone { get; set; }

    public string? Mobile { get; set; }

    public string? Email { get; set; }

    public string? AddressLine { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Website { get; set; }

    /// <summary>Pagina in de Drammerskrant (kolom PAGINA); soms een opmerking zoals "2025 niet".</summary>
    public string? Page { get; set; }

    public AdvertiserKind Kind { get; set; }

    public AdvertiserPayment Payment { get; set; }

    /// <summary>IBAN, versleuteld (Data Protection).</summary>
    public string? IbanProtected { get; set; }

    public string? IbanLast4 { get; set; }

    /// <summary>Kenmerk van de SEPA-machtiging (kolom SEPA MACHTIGINGS-NUM).</summary>
    public string? MandateReference { get; set; }

    /// <summary>Het lid (kaderlid) dat langsgaat.</summary>
    public Guid? CollectorMemberId { get; set; }

    /// <summary>De naam van de collectant zoals in het Excel-bestand, als die (nog) niet aan een kaderlid is gekoppeld.</summary>
    public string? ImportedCollectorName { get; set; }

    public string? Notes { get; set; }

    /// <summary>Niet meer actief: telt niet mee in de campagne.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Via de app aangemeld door een collectant; het bestuur kijkt de gegevens na.</summary>
    public bool AddedViaApp { get; set; }

    public List<AdvertiserYear> Years { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Bijdrage en stand van een adverteerder in één jaar (kolommen BIJDRAGE 2001 … in het overzicht).</summary>
public sealed class AdvertiserYear
{
    public Guid AdvertiserId { get; set; }

    public int Year { get; set; }

    /// <summary>Het bedrag; <c>null</c> als het (nog) niet bekend is.</summary>
    public decimal? Amount { get; set; }

    /// <summary>Gratis (in het overzicht "GRATIS").</summary>
    public bool IsFree { get; set; }

    public AdvertiserYearStatus Status { get; set; }

    /// <summary>Wanneer en door wie de stand voor het laatst in het portal of de app is gezet (niet door de import).</summary>
    public DateTime? StatusChangedAt { get; set; }

    public Guid? StatusChangedBy { get; set; }

    /// <summary>Opmerking van dit jaar, bijv. van de collectant in de app (fase 27g ook in de export).</summary>
    public string? Note { get; set; }

    /// <summary>Ronde 1, 2 of 3 waarin de adverteerder dit jaar meegaat (fase 27g, in het portal); kleurt de regel in de export.</summary>
    public byte? Round { get; set; }

    /// <summary>Bij contante betaling: wanneer en door wie het geld is ontvangen (fase 27d).</summary>
    public DateTime? PaidAt { get; set; }

    public Guid? PaidBy { get; set; }
}

/// <summary>
/// Factuur aan een adverteerder voor een campagnejaar (fase 27e): één per adverteerder per jaar, met een vast nummer
/// (ADV-jaar-volgnummer). De gegevens van de adverteerder worden vastgelegd, zodat de PDF later precies hetzelfde is.
/// </summary>
public sealed class AdvertiserInvoice
{
    public Guid Id { get; set; }

    public Guid AdvertiserId { get; set; }

    public int Year { get; set; }

    /// <summary>Bijvoorbeeld ADV-2027-0001.</summary>
    public required string Number { get; set; }

    public int Sequence { get; set; }

    public DateOnly InvoiceDate { get; set; }

    public decimal Amount { get; set; }

    public required string Description { get; set; }

    public AdvertiserPayment Payment { get; set; }

    public required string CompanyName { get; set; }

    public string? ContactName { get; set; }

    public string? AddressLine { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Email { get; set; }

    public string? MandateReference { get; set; }

    public string? IbanLast4 { get; set; }

    /// <summary>Bij contant: wanneer het geld al was ontvangen (op de factuur als "voldaan").</summary>
    public DateTime? PaidAt { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
