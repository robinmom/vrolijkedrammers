using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Content.Festival;

/// <summary>
/// Een editie van het Dansgardefestival (2026-10-12): datum, inschrijfperiode, plekken, tarieven en documenten. Precies één
/// editie is actief; die staat (als de pagina aan staat) op de website onder Doe mee.
/// </summary>
public sealed class DanceFestival : IAuditable
{
    public Guid Id { get; set; }

    /// <summary>Bijvoorbeeld <c>Dansgardefestival 2027</c>.</summary>
    public required string Name { get; set; }

    public DateOnly FestivalDate { get; set; }

    public required string Location { get; set; }

    /// <summary>De pagina staat op de website (los van de inschrijfperiode).</summary>
    public bool PageEnabled { get; set; }

    /// <summary>Inschrijven kan vanaf (UTC); leeg = nog niet open.</summary>
    public DateTime? RegistrationOpensAt { get; set; }

    /// <summary>Inschrijven kan tot (UTC).</summary>
    public DateTime? RegistrationClosesAt { get; set; }

    /// <summary>Aantal plekken: één plek per goedgekeurde dans.</summary>
    public int Capacity { get; set; }

    /// <summary>Betaallinks (iDEAL via Mollie) na goedkeuring.</summary>
    public bool PaymentsEnabled { get; set; }

    public int PriceSoloDuoCents { get; set; }

    public int PriceGroupOneDanceCents { get; set; }

    public int PriceGroupTwoDancesCents { get; set; }

    /// <summary>Btw-tarief op het deelnamegeld (instelbaar in de portal, standaard 0,21). Het ingevulde bedrag is excl. btw.</summary>
    public decimal VatRate { get; set; } = 0.21m;

    /// <summary>Servicekosten (betaling via iDEAL) doorberekenen op het betaalverzoek.</summary>
    public bool ServiceFeeOn { get; set; }

    /// <summary>Servicekosten in centen (excl. btw) die erbij komen als de switch aanstaat.</summary>
    public int ServiceFeeCents { get; set; }

    /// <summary>Hier komen meldingen van nieuwe en gewijzigde inschrijvingen binnen; ook het antwoordadres in mails.</summary>
    public required string NotificationEmail { get; set; }

    /// <summary>Reglement (PDF) in de container <c>content</c>.</summary>
    public string? RulesBlobPath { get; set; }

    /// <summary>Privacyverklaring (PDF) in de container <c>content</c>.</summary>
    public string? PrivacyBlobPath { get; set; }

    /// <summary>Begin van het programma (de opening); de rest sluit daarop aan.</summary>
    public TimeOnly ProgramStart { get; set; } = new(10, 30);

    /// <summary>Wanneer de volgorde binnen de categorieën is geloot (leeg = nog niet).</summary>
    public DateTime? DrawnAt { get; set; }

    /// <summary>De uitslag staat voor de hoofdjury klaar (in de app) voor de prijsuitreiking.</summary>
    public DateTime? HeadJuryPublishedAt { get; set; }

    /// <summary>De prijsuitreiking is afgerond (door de hoofdjury, of overruled in het portal).</summary>
    public DateTime? AwardsCompletedAt { get; set; }

    /// <summary>"hoofdjury" of "organisatie".</summary>
    public string? AwardsCompletedBy { get; set; }

    /// <summary>Het nieuwsbericht met de uitslag op de website (na de prijsuitreiking).</summary>
    public Guid? ResultsNewsId { get; set; }

    /// <summary>Wanneer de rapporten naar de verenigingen zijn klaargezet om te versturen.</summary>
    public DateTime? ReportsSentAt { get; set; }

    /// <summary>Laatst uitgegeven inschrijfnummer (UPDATE … OUTPUT, zoals bij de optocht).</summary>
    public int LastRegistrationNumber { get; set; }

    public bool Active { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsOpenOn(DateTime utcNow) =>
        PageEnabled && RegistrationOpensAt is { } from && from <= utcNow && (RegistrationClosesAt is null || utcNow < RegistrationClosesAt);
}

/// <summary>Inschrijving van een vereniging met één of meer dansen.</summary>
public sealed class DanceFestivalRegistration
{
    public Guid Id { get; set; }

    public Guid FestivalId { get; set; }

    /// <summary>Inschrijfnummer per editie (volgorde van binnenkomst, "vol = vol").</summary>
    public int Number { get; set; }

    public required string ClubName { get; set; }

    public required string ContactName { get; set; }

    public required string AddressLine { get; set; }

    public required string PostalCode { get; set; }

    public required string City { get; set; }

    public required string Phone { get; set; }

    public required string Email { get; set; }

    public string? Remarks { get; set; }

    /// <summary>Toestemming voor foto's en video's van de optredens (privacyverklaring).</summary>
    public bool PhotoConsent { get; set; }

    public DateTime PrivacyAcceptedAt { get; set; }

    public DateTime RulesAcceptedAt { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? ChangedAt { get; set; }

    /// <summary>Oploopnummer; elke wijziging door de vereniging bewaart de vorige versie.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Wijzig-/betaaltoken voor de vereniging, versleuteld (Data Protection) zodat het opnieuw gemaild kan worden.</summary>
    public required string AccessTokenProtected { get; set; }

    /// <summary>Laatste vraag om meer informatie (voor de mail en de wijzigpagina).</summary>
    public string? InfoRequest { get; set; }

    /// <summary>Het juryrapport is verstuurd (na de prijsuitreiking).</summary>
    public DateTime? ReportSentAt { get; set; }

    public List<DanceFestivalEntry> Entries { get; set; } = [];
}

public enum DanceType
{
    Garde,
    Show,
}

public enum DanceFormat
{
    Solo,
    Duo,
    Group,
}

public enum ShowStyle
{
    Character,
    Modern,
}

/// <summary>Categorie op gemiddelde leeftijd (reglement art. 1).</summary>
public enum DanceCategory
{
    Minioren,
    Junioren,
    Jeugd,
    Senioren,
}

public enum DanceEntryStatus
{
    New,
    InfoRequested,
    Approved,
    Waitlisted,
    Rejected,

    /// <summary>Gewijzigd door de vereniging na een beslissing: opnieuw beoordelen (een goedgekeurde dans houdt zijn plek).</summary>
    Changed,
}

/// <summary>Eén dans (optreden) van een inschrijving; beoordelen gaat per dans.</summary>
public sealed class DanceFestivalEntry
{
    public Guid Id { get; set; }

    public Guid RegistrationId { get; set; }

    public int SortOrder { get; set; }

    public DanceType DanceType { get; set; }

    public DanceFormat Format { get; set; }

    /// <summary>Naam van de groep of namen van de danser(s).</summary>
    public required string Names { get; set; }

    public decimal AverageAge { get; set; }

    public int DancerCount { get; set; }

    public required string LeaderName { get; set; }

    /// <summary>Alleen showdans.</summary>
    public string? Title { get; set; }

    /// <summary>Alleen showdans.</summary>
    public ShowStyle? ShowStyle { get; set; }

    public DanceCategory Category { get; set; }

    public DanceEntryStatus Status { get; set; }

    /// <summary>De dans telt mee voor de plekken (goedgekeurd, ook na een wijziging die nog beoordeeld moet worden).</summary>
    public bool HoldsPlace { get; set; }

    /// <summary>Volgorde op de wachtlijst (1 = eerste); alleen bij <see cref="DanceEntryStatus.Waitlisted"/>.</summary>
    public int? WaitlistPosition { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>Door de vereniging verwijderd na een beslissing (blijft zichtbaar in de geschiedenis).</summary>
    public DateTime? RemovedAt { get; set; }

    /// <summary>Volgorde binnen de programmacategorie (loting of verschuiven); leeg = op inschrijfnummer.</summary>
    public int? ProgramOrder { get; set; }

    public byte[] RowVersion { get; set; } = [];

    /// <summary>Categorie uit de gemiddelde leeftijd: t/m 8 minioren, 9–11 junioren, 12–15 jeugd, 16+ senioren.</summary>
    public static DanceCategory CategoryFor(decimal averageAge) => averageAge switch
    {
        < 9m => DanceCategory.Minioren,
        < 12m => DanceCategory.Junioren,
        < 16m => DanceCategory.Jeugd,
        _ => DanceCategory.Senioren,
    };
}

/// <summary>Eerdere versie van een inschrijving (bij elke wijziging door de vereniging), als JSON.</summary>
public sealed class DanceFestivalRegistrationVersion
{
    public Guid Id { get; set; }

    public Guid RegistrationId { get; set; }

    public int Version { get; set; }

    public required string Snapshot { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum DanceFestivalPaymentStatus
{
    Open,
    Paid,

    /// <summary>Ingetrokken door de organisatie (bijv. verkeerd bedrag).</summary>
    Canceled,
}

/// <summary>
/// Betaalverzoek na goedkeuring: het bedrag vult de organisatie in. De link in de mail hergebruikt een Mollie-betaling
/// die nog open staat en maakt anders een nieuwe (<see cref="DanceFestivalMolliePayment"/>); de status komt altijd van
/// Mollie (webhook of "status vernieuwen").
/// </summary>
public sealed class DanceFestivalPayment
{
    public Guid Id { get; set; }

    public Guid RegistrationId { get; set; }

    /// <summary>Het totaal inclusief btw en servicekosten: dit bedrag wordt bij Mollie afgerekend.</summary>
    public int AmountCents { get; set; }

    /// <summary>Het deelnamegeld exclusief btw (door de organisatie ingevuld).</summary>
    public int NetCents { get; set; }

    /// <summary>De btw over het deelnamegeld.</summary>
    public int VatCents { get; set; }

    /// <summary>Doorberekende servicekosten exclusief btw (0 als de switch uitstaat).</summary>
    public int ServiceFeeNetCents { get; set; }

    /// <summary>De btw over de servicekosten.</summary>
    public int ServiceFeeVatCents { get; set; }

    /// <summary>Vast factuurnummer (FEST-jaar-volgnr) voor de factuur-PDF; leeg bij betaalverzoeken van vóór fase 2.</summary>
    public string? InvoiceNumber { get; set; }

    public DanceFestivalPaymentStatus Status { get; set; }

    /// <summary>Laatste Mollie-betaling (<c>tr_…</c>) voor dit verzoek.</summary>
    public string? MolliePaymentId { get; set; }

    /// <summary>Laatste status bij Mollie (open, paid, expired, canceled, failed).</summary>
    public string? MollieStatus { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? PaidAt { get; set; }
}

/// <summary>
/// Eén Mollie-betaling (<c>tr_…</c>) voor een betaalverzoek. Alle betalingen blijven bekend, zodat de webhook ook een
/// oudere betaling herkent; wordt een verzoek twee keer betaald, dan staat <see cref="DuplicatePaid"/> aan voor de
/// penningmeester (terugbetalen).
/// </summary>
public sealed class DanceFestivalMolliePayment
{
    public Guid Id { get; set; }

    public Guid PaymentId { get; set; }

    public required string MolliePaymentId { get; set; }

    /// <summary>Laatst bekende status bij Mollie.</summary>
    public string? MollieStatus { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? PaidAt { get; set; }

    public bool DuplicatePaid { get; set; }
}
