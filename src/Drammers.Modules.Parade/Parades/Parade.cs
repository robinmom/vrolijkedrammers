using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Parade.Parades;

public enum ParadeStatus
{
    Planned,
    RegistrationOpen,
    RegistrationClosed,
    Composing,
    Final,
    Completed,
}

/// <summary>
/// Een optocht per carnavalsjaar (docs/14 §2). Of inschrijven kan, volgt uit de inschrijfperiode; de status is voor de
/// commissie (samenstellen en vaststellen, fase 12).
/// </summary>
public sealed class Parade : IAuditable
{
    public Guid Id { get; set; }

    public int CarnivalYearId { get; set; }

    public required string Name { get; set; }

    public DateOnly ParadeDate { get; set; }

    public TimeOnly StartTime { get; set; }

    public string? StartLocation { get; set; }

    public string? RouteDescription { get; set; }

    public decimal? RouteLengthKm { get; set; }

    public DateTime RegistrationOpensAt { get; set; }

    public DateTime RegistrationClosesAt { get; set; }

    /// <summary>Na dit moment beperkt wijzigbaar (volgens het statusbeleid); leeg = sluiting van de inschrijving.</summary>
    public DateTime? EditDeadlineAt { get; set; }

    /// <summary>Onderwerp verplicht (OQ-13: standaard ja).</summary>
    public bool SubjectRequired { get; set; } = true;

    public decimal DefaultSpacingMeters { get; set; } = 5m;

    public int MaxDocumentsPerRegistration { get; set; } = 5;

    public int MaxDocumentSizeMb { get; set; } = 10;

    /// <summary>
    /// Informatie over de optocht en het inschrijven (Markdown), door het bestuur of de commissie in het portal beheerd;
    /// leden zonder de rol Groepsverantwoordelijke zien alleen deze pagina.
    /// </summary>
    public string? InfoText { get; set; }

    public ParadeStatus Status { get; set; }

    /// <summary>
    /// Vaste plekken vooraan (fase 12c): elk jaar hetzelfde, zoals de geluidswagen, de verenigingswagen en het Convent.
    /// Ze krijgen startnummer 1, 2, …; de startnummers van de groepen beginnen daarna.
    /// </summary>
    public List<ParadeFixedEntry> FixedEntries { get; set; } = [];

    /// <summary>Meldplek voor de wagens (fase 16), bijv. "Rotonde Holthuizen"; kolomkop van de aanrijtijdenlijst.</summary>
    public string? ArrivalLocation { get; set; }

    /// <summary>Aanrijtijden gepubliceerd: groepen, de app en de openbare lijst zien ze vanaf dan.</summary>
    public DateTime? ArrivalTimesPublishedAt { get; set; }

    /// <summary>Het eerste startnummer dat een groep kan krijgen.</summary>
    public int FirstGroupStartNumber => FixedEntries.Count + 1;

    /// <summary>
    /// Uitslag gepubliceerd (fase 22c): pas daarna staat de uitslag op de website en in de app. Alleen na de
    /// prijsuitreiking; daarna is de optocht afgerond.
    /// </summary>
    public DateTime? ResultsPublishedAt { get; set; }

    public Guid? ResultsPublishedBy { get; set; }

    /// <summary>Album met foto's van de inzendingen bij de uitslag (fase 22d); gepubliceerd samen met de uitslag.</summary>
    public Guid? ResultsAlbumId { get; set; }

    /// <summary>Optimistic concurrency voor het samenstellen (fase 12, ADR-012).</summary>
    public int CompositionVersion { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsRegistrationOpen(DateTime now) =>
        (Status is ParadeStatus.Planned or ParadeStatus.RegistrationOpen) && now >= RegistrationOpensAt && now < RegistrationClosesAt;
}

/// <summary>Teller voor opgavenummers (ADR-011): één rij per optocht, alleen atomair opgehoogd in de submit-transactie.</summary>
public sealed class ParadeNumberSequence
{
    public Guid ParadeId { get; set; }

    public int LastRegistrationNumber { get; set; }
}

/// <summary>Een vaste plek vooraan in de optocht (startnummer = positie).</summary>
public sealed class ParadeFixedEntry
{
    public const int MaxCount = 10;

    public required string Name { get; set; }

    public int AdultCount { get; set; }

    public int ChildrenCount { get; set; }

    public bool HasMusic { get; set; }

    /// <summary>De standaard vaste plekken van De Vrolijke Drammers.</summary>
    public static List<ParadeFixedEntry> Defaults() =>
    [
        new() { Name = "Geluidswagen", AdultCount = 2, HasMusic = true },
        new() { Name = "Verenigingswagen \"de Vrolijke Drammers\"", AdultCount = 14, HasMusic = true },
        new() { Name = "Het Convent van \"de Vrolijke Drammers\"", AdultCount = 8, HasMusic = false },
    ];
}
