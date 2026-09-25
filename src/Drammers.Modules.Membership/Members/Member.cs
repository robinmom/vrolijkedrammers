using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Membership.Members;

public enum MembershipStatus
{
    Active,
    Inactive,
    Suspended,
    Deceased,
}

public enum MemberSyncState
{
    InSync,
    Missing,
    Conflict,
}

/// <summary>
/// Lokale kopie van een lid uit e-Boekhouden (docs/04 §4, ADR-010). De e-Boekhouden-velden worden alleen door de
/// sync gezet; lokale velden (override, geldigheid, naamcorrectie) raakt de sync nooit aan. IBAN, BIC, mandaat,
/// notities en factuuradressen worden bewust niet opgeslagen (dataminimalisatie).
/// </summary>
public sealed class Member : IAuditable
{
    public Guid Id { get; set; }

    // --- e-Boekhouden (alleen de sync) ---
    public required string MemberNumber { get; set; }

    public int? EbMemberId { get; set; }

    public required string FullName { get; set; }

    public string? Salutation { get; set; }

    public string? Gender { get; set; }

    public string? AddressLine { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? MobilePhone { get; set; }

    /// <summary>Uit een vrij veld (mapping) of lokaal ingevuld als het niet gemapt is.</summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>Inschrijfjaar (jubilarissen); uit een vrij veld of lokaal.</summary>
    public short? JoinYear { get; set; }

    /// <summary>Ruwe statuswaarde uit het gemapte vrije veld, bijv. "opgezegd".</summary>
    public string? EbStatusRaw { get; set; }

    public string? MemberCategory { get; set; }

    // --- Afgeleid ---
    public string? FirstName { get; set; }

    public string? NamePrefix { get; set; }

    public string? LastName { get; set; }

    /// <summary>Naamdelen handmatig gecorrigeerd: de sync leidt ze dan niet opnieuw af.</summary>
    public bool NameCorrectedManually { get; set; }

    public MembershipStatus MembershipStatus { get; set; }

    // --- Lokaal ---
    public MembershipStatus? LocalStatusOverride { get; set; }

    public DateOnly? MembershipValidFrom { get; set; }

    public DateOnly? MembershipValidTo { get; set; }

    // --- Sync ---
    public byte[] EbHash { get; set; } = [];

    public DateTime? EbLastSeenAt { get; set; }

    public DateTime? EbMissingSince { get; set; }

    public MemberSyncState SyncState { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>De status die telt: een lokale override wint altijd van de afleiding uit de sync (ADR-010).</summary>
    public MembershipStatus EffectiveStatus => LocalStatusOverride ?? MembershipStatus;
}
