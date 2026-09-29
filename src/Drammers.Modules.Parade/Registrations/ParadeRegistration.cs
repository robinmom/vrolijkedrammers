using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Parade.Registrations;

public enum RegistrationStatus
{
    Draft,
    Submitted,
    UnderReview,
    AdditionalInformationRequired,
    Approved,
    Rejected,
    Withdrawn,
    StartNumberAssigned,
    Final,
}

public enum RegistrationSource
{
    App,
    WebForm,
    Portal,
}

/// <summary>Adres als owned value object (docs/14 §5).</summary>
public sealed class Address
{
    public string? Street { get; set; }

    public string? HouseNumber { get; set; }

    public string? Addition { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string Country { get; set; } = "NL";

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Street) && string.IsNullOrWhiteSpace(HouseNumber) && string.IsNullOrWhiteSpace(PostalCode) && string.IsNullOrWhiteSpace(City);
}

/// <summary>
/// Inschrijving van een groep voor de optocht (docs/14 §4). Het opgavenummer ontstaat alleen bij definitief indienen
/// (ADR-011) en is daarna voor niemand te wijzigen; een concept heeft er nooit een.
/// </summary>
public sealed class ParadeRegistration : IAuditable
{
    public Guid Id { get; set; }

    public Guid ParadeId { get; set; }

    public int CarnivalYearId { get; set; }

    public int? RegistrationNumber { get; set; }

    public int? StartNumber { get; set; }

    public int? ParadeOrder { get; set; }

    public string? GroupName { get; set; }

    public string? ContactName { get; set; }

    /// <summary>E.164, bijv. <c>+31612345678</c>.</summary>
    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }

    public int? CategoryId { get; set; }

    public string? Subject { get; set; }

    public string? SubjectDescription { get; set; }

    public int ChildrenCount { get; set; }

    public int AdultCount { get; set; }

    /// <summary>Muziek bij de groep (fase 12c, kolom Muziek in de export); leeg bij inschrijvingen van vóór deze vraag.</summary>
    public bool? HasMusic { get; set; }

    /// <summary>Aanrijtijd bij de meldplek van de optocht (fase 16, alleen wagens); zichtbaar na publiceren.</summary>
    public TimeOnly? ArrivalTime { get; set; }

    public Address BuildAddress { get; set; } = new();

    public bool JuryInspectionSameAsBuildAddress { get; set; } = true;

    public Address JuryInspectionAddress { get; set; } = new();

    public decimal? EstimatedLengthMeters { get; set; }

    public decimal? MeasuredLengthMeters { get; set; }

    public decimal? SpacingAfterMeters { get; set; }

    public string? AdditionalInformation { get; set; }

    public RegistrationStatus Status { get; set; }

    /// <summary>Actuele waarschuwingen (JSON), bijv. deelnemers buiten bereik bij een categorie met <c>Warn</c>.</summary>
    public string? ValidationWarnings { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? WithdrawnAt { get; set; }

    public Guid? OwnerUserId { get; set; }

    public DateTime? ContactEmailVerifiedAt { get; set; }

    /// <summary>Webformulier/gast: hash van de e-mailcode (verloopt na 30 minuten, maximaal 5 pogingen).</summary>
    public string? VerificationCodeHash { get; set; }

    public DateTime? VerificationExpiresAt { get; set; }

    public int VerificationAttempts { get; set; }

    /// <summary>Webformulier/gast: SHA-256 (hex) van het token van de statuslink (alleen lezen).</summary>
    public string? StatusTokenHash { get; set; }

    public RegistrationSource Source { get; set; }

    /// <summary>Herinnering "de inschrijving sluit bijna" is verstuurd (alleen concepten, één keer).</summary>
    public DateTime? DeadlineReminderSentAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Het juryadres zoals het geldt: bij "gelijk aan bouwadres" het bouwadres (niet gekopieerd, docs/14 §5).</summary>
    public Address EffectiveJuryAddress => JuryInspectionSameAsBuildAddress ? BuildAddress : JuryInspectionAddress;

    public static readonly RegistrationStatus[] Active =
    [
        RegistrationStatus.Submitted, RegistrationStatus.UnderReview, RegistrationStatus.AdditionalInformationRequired,
        RegistrationStatus.Approved, RegistrationStatus.StartNumberAssigned, RegistrationStatus.Final,
    ];
}

public enum ManagerRole
{
    Owner,
    CoManager,
}

/// <summary>Leden die een inschrijving mogen beheren (docs/14 §9); alleen voor inschrijvingen via de app.</summary>
public sealed class ParadeRegistrationManager
{
    public Guid RegistrationId { get; set; }

    public Guid UserId { get; set; }

    public ManagerRole Role { get; set; }

    public DateTime AddedAt { get; set; }
}

public sealed class ParadeStatusHistory
{
    public long Id { get; set; }

    public Guid RegistrationId { get; set; }

    public RegistrationStatus? FromStatus { get; set; }

    public RegistrationStatus ToStatus { get; set; }

    public string? Reason { get; set; }

    public Guid? ActorUserId { get; set; }

    public DateTime OccurredAt { get; set; }
}

/// <summary>Wijziging per veld (docs/14 §6), gevuld door een SaveChanges-interceptor.</summary>
public sealed class ParadeRegistrationHistory
{
    public long Id { get; set; }

    public Guid RegistrationId { get; set; }

    public required string FieldName { get; set; }

    public required string FieldLabel { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public Guid? ChangedByUserId { get; set; }

    public DateTime ChangedAt { get; set; }

    public RegistrationSource ChangeSource { get; set; }

    public Guid CorrelationId { get; set; }
}

public enum DocumentType
{
    Insurance,
    VehicleInspection,
    Drawing,
    Other,
}

/// <summary>Document bij een inschrijving (docs/14 §9), in de private container <c>parade-documents</c>.</summary>
public sealed class ParadeDocument
{
    public Guid Id { get; set; }

    public Guid RegistrationId { get; set; }

    public DocumentType DocumentType { get; set; }

    public required string FileName { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }

    public required string BlobPath { get; set; }

    public Guid? UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; }
}

public enum ActorScope
{
    Owner,
    Committee,
    SpecialAdmin,
}

/// <summary>
/// Welke velden wie per status mag wijzigen (docs/14 §9); <c>*</c> = alles behalve het opgavenummer. Leeg
/// <see cref="ParadeId"/> = standaardbeleid.
/// </summary>
public sealed class ParadeStatusEditPolicy
{
    public int Id { get; set; }

    public Guid? ParadeId { get; set; }

    public RegistrationStatus Status { get; set; }

    public ActorScope ActorScope { get; set; }

    /// <summary>Kommagescheiden veldnamen of <c>*</c>.</summary>
    public required string EditableFields { get; set; }

    public bool CanWithdraw { get; set; }
}
