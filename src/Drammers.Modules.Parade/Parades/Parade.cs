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

    public ParadeStatus Status { get; set; }

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
