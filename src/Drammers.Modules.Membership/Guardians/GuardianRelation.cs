namespace Drammers.Modules.Membership.Guardians;

public enum GuardianRelationship
{
    Parent,
    Caregiver,
}

/// <summary>
/// Ouder/verzorger van een minderjarig lid (ADR-014 §3, fase 17). Eén ouderaccount kan aan meerdere kinderen gekoppeld
/// zijn; een kind heeft hooguit twee ouders/verzorgers. De ouder is niet per se zelf lid. De koppeling telt tot het kind
/// 18 wordt; heeft het kind een eigen account (vanaf 15), dan krijgt de ouder nog meldingen maar toont hij de QR niet meer.
/// </summary>
public sealed class GuardianRelation
{
    public const int MaxPerChild = 2;

    public const int AdultAge = 18;

    public Guid Id { get; set; }

    public Guid MemberId { get; set; }

    /// <summary>Account van de ouder (<c>identity.User</c>).</summary>
    public Guid GuardianUserId { get; set; }

    public required string GuardianName { get; set; }

    public string? GuardianPhone { get; set; }

    public GuardianRelationship Relationship { get; set; }

    /// <summary>Bevestigd via de e-mailverificatie van de aanmelding of door het bestuur.</summary>
    public DateTime VerifiedAt { get; set; }

    /// <summary>Bestuurder die de koppeling maakte (leeg bij een aanmelding via Lid worden).</summary>
    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum GuardianLinkRequestStatus
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// Koppelverzoek van een ouder in de app (fase 17), op voor- en achternaam van het kind. Pas na goedkeuring door het
/// bestuur in het portal ontstaat een <see cref="GuardianRelation"/>; het bestuur kiest dan het juiste lid.
/// </summary>
public sealed class GuardianLinkRequest
{
    public Guid Id { get; set; }

    public Guid RequestedByUserId { get; set; }

    public required string ChildFirstName { get; set; }

    public required string ChildLastName { get; set; }

    public GuardianRelationship Relationship { get; set; }

    public string? Phone { get; set; }

    public GuardianLinkRequestStatus Status { get; set; }

    /// <summary>Het lid dat het bestuur bij goedkeuring koos.</summary>
    public Guid? MemberId { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public Guid? DecidedBy { get; set; }
}

/// <summary>"Geen relatie" bij een voorstel op basis van hetzelfde e-mailadres: het voorstel komt niet terug.</summary>
public sealed class GuardianSuggestionDismissal
{
    public Guid ChildMemberId { get; set; }

    public Guid ParentMemberId { get; set; }

    public DateTime DismissedAt { get; set; }

    public Guid? DismissedBy { get; set; }
}
