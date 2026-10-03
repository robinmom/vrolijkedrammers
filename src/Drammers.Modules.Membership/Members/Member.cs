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
/// sync gezet; lokale velden (override, geldigheid, naamcorrectie) raakt de sync nooit aan. Sinds fase 26 staan IBAN en
/// machtiging lokaal (versleuteld) voor leden die ze via de app opgeven; BIC, notities en factuuradressen worden niet
/// opgeslagen (dataminimalisatie).
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

    /// <summary>Naam van het tweede lid bij een tweepersoonslidmaatschap (vrij veld uit e-Boekhouden, fase 25); vult het splitsformulier vooraf in.</summary>
    public string? SecondMemberName { get; set; }

    /// <summary>Naam van de optochtgroep van dit lid (vrij veld uit e-Boekhouden, fase 11); vult de inschrijving vooraf in.</summary>
    public string? ParadeGroupName { get; set; }

    // --- Afgeleid ---
    public string? FirstName { get; set; }

    public string? NamePrefix { get; set; }

    public string? LastName { get; set; }

    /// <summary>
    /// Velden uit e-Boekhouden die in het portal zijn aangepast (komma-gescheiden sleutels, zie
    /// <see cref="MemberFields"/>). De sync overschrijft ze niet meer, maar meldt het als e-Boekhouden afwijkt.
    /// </summary>
    public string? LocalFields { get; set; }

    public bool IsLocal(string field) =>
        LocalFields is { Length: > 0 } fields && fields.Split(',').Contains(field, StringComparer.Ordinal);

    /// <summary>Naamdelen handmatig gecorrigeerd: de sync leidt ze dan niet opnieuw af.</summary>
    public bool NameCorrectedManually { get; set; }

    public MembershipStatus MembershipStatus { get; set; }

    // --- Lokaal ---
    public MembershipStatus? LocalStatusOverride { get; set; }

    public DateOnly? MembershipValidFrom { get; set; }

    public DateOnly? MembershipValidTo { get; set; }

    /// <summary>
    /// Jubileum telt vanaf dit jaar in plaats van het inschrijfjaar (fase 20), bijvoorbeeld als iemand een tijd geen lid was.
    /// </summary>
    public short? JubileeJoinYearOverride { get; set; }

    /// <summary>Waarom het jubileumjaar is aangepast.</summary>
    public string? JubileeNote { get; set; }

    /// <summary>Soort lidmaatschap (fase 23); leeg = afgeleid uit e-Boekhouden (<see cref="EbStatusRaw"/>).</summary>
    public MembershipKind? MembershipKind { get; set; }

    /// <summary>Bij een partner in een tweepersoonslidmaatschap: het lid dat betaalt.</summary>
    public Guid? PayerMemberId { get; set; }

    /// <summary>Betaalt geen contributie, bijvoorbeeld Convent (bewezen dienstjaren); handmatig toegekend.</summary>
    public bool ContributionExempt { get; set; }

    public string? ContributionExemptReason { get; set; }

    // --- Bankgegevens (fase 26; lokaal, versleuteld met Data Protection) ---

    /// <summary>IBAN, versleuteld; alleen in te zien met <c>contribution.manage</c>.</summary>
    public string? IbanProtected { get; set; }

    /// <summary>Laatste 4 tekens van de IBAN, voor de weergave.</summary>
    public string? IbanLast4 { get; set; }

    public string? AccountHolder { get; set; }

    /// <summary>Kenmerk van de machtiging (max. 35 tekens, SEPA).</summary>
    public string? MandateReference { get; set; }

    public DateOnly? MandateSignedOn { get; set; }

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

    /// <summary>Het jaar waarvanaf het jubileum telt: de correctie wint van het inschrijfjaar.</summary>
    public short? JubileeBaseYear => JubileeJoinYearOverride ?? JoinYear;
}
