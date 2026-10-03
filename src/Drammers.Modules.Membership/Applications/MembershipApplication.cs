namespace Drammers.Modules.Membership.Applications;

/// <summary>Statussen van een aanmelding (ADR-014, docs/04 §4). Nooit automatisch een definitief lidmaatschap.</summary>
public enum ApplicationStatus
{
    /// <summary>Formulier ingevuld, e-mailadres nog niet bevestigd; na 7 dagen opgeruimd.</summary>
    Draft,

    /// <summary>E-mailadres bevestigd en daarmee ingediend: wacht op het bestuur.</summary>
    Submitted,

    InReview,
    Approved,
    Rejected,
    Provisioning,
    ProvisioningFailed,

    /// <summary>Lid aangemaakt in e-Boekhouden en lokaal, account klaar en welkomstmail verstuurd.</summary>
    Activated,
    Withdrawn,
}

public enum ApplicationSource
{
    App,
    Website,
    Portal,
}

/// <summary>
/// Aanmelding als nieuw lid (fase 9b, REQ-APP-01..05). Leeft in een tijdelijke wachtrij tot het bestuur beslist. De
/// bankgegevens staan hier alleen tot ze naar e-Boekhouden zijn doorgegeven of de aanvraag is afgewezen, en worden dan
/// gewist (dataminimalisatie).
/// </summary>
/// <summary>Soort lidmaatschap bij Lid worden (fase 17): gewoon lid of dansgarde (vrij veld "groep" in e-Boekhouden).</summary>
public enum MembershipType
{
    Individual,
    Dansgarde,
}

public sealed class MembershipApplication
{
    public Guid Id { get; set; }

    public ApplicationStatus Status { get; set; }

    public ApplicationSource Source { get; set; }

    public MembershipType MembershipType { get; set; }

    // --- Aanvrager (het nieuwe lid) ---
    public required string FirstName { get; set; }

    public string? NamePrefix { get; set; }

    public required string LastName { get; set; }

    /// <summary><c>m</c>, <c>v</c> of leeg (e-Boekhouden: m/v/a).</summary>
    public string? Gender { get; set; }

    public DateOnly BirthDate { get; set; }

    public required string AddressLine { get; set; }

    public required string PostalCode { get; set; }

    public required string City { get; set; }

    /// <summary>E-mailadres van het lid; bij een minderjarige het adres van de ouder/verzorger.</summary>
    public required string Email { get; set; }

    public string? Phone { get; set; }

    // --- Ouder/verzorger (verplicht onder de 16) ---
    public string? GuardianName { get; set; }

    public string? GuardianEmail { get; set; }

    public string? GuardianPhone { get; set; }

    // --- Contributie (SEPA-machtiging, doorlopend) ---
    public string? Iban { get; set; }

    public string? AccountHolder { get; set; }

    public DateTime? MandateConsentAt { get; set; }

    /// <summary>Kenmerk van de machtiging, vast bij het indienen (max. 35 tekens, e-Boekhouden <c>mandateId</c>).</summary>
    public required string MandateReference { get; set; }

    // --- Toestemmingen ---
    public DateTime ConsentPrivacyAt { get; set; }

    public bool ConsentPhoto { get; set; }

    // --- E-mailverificatie ---
    public string? VerificationCodeHash { get; set; }

    public DateTime? VerificationExpiresAt { get; set; }

    public int VerificationAttempts { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    // --- Afhandeling ---
    public DateTime CreatedAt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public Guid? HandledBy { get; set; }

    public DateTime? HandledAt { get; set; }

    public DateTime? DecisionAt { get; set; }

    public string? RejectionReason { get; set; }

    public string? InternalNotes { get; set; }

    /// <summary>
    /// Lid splitsen (fase 25): dit is het tweede lid van het tweepersoonslidmaatschap van dit hoofdlid. Na goedkeuring
    /// wordt het nieuwe lid de partner van het hoofdlid (combinatie, het hoofdlid betaalt); geen IBAN of machtiging nodig.
    /// </summary>
    public Guid? SplitFromMemberId { get; set; }

    public Guid? ResultingMemberId { get; set; }

    public Guid? ProvisioningId { get; set; }

    public string? IpHash { get; set; }

    public bool IsMinorOn(DateOnly date) => AgeOn(date) < MinimumAgeOwnAccount;

    public int AgeOn(DateOnly date)
    {
        var age = date.Year - BirthDate.Year;
        return BirthDate > date.AddYears(-age) ? age - 1 : age;
    }

    /// <summary>OQ-15 (besluit 2026-09-29, fase 17): vanaf 15 een eigen account; jonger via de ouder/verzorger.</summary>
    public const int MinimumAgeOwnAccount = 15;

    /// <summary>Lid worden kan vanaf 5 jaar (dansgarde).</summary>
    public const int MinimumAgeMembership = 5;

    public string FullName => string.Join(' ', new[] { FirstName, NamePrefix, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
}
