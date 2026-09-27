namespace Drammers.Modules.Membership.Guardians;

/// <summary>
/// Ouder/verzorger van een minderjarig lid (ADR-014 §3, minimaal voor fase 9b; beheer en meldingen volgen in fase 17).
/// Eén ouderaccount kan aan meerdere kinderen gekoppeld zijn. De ouder is niet per se zelf lid.
/// </summary>
public sealed class GuardianRelation
{
    public Guid Id { get; set; }

    public Guid MemberId { get; set; }

    /// <summary>Account van de ouder (<c>identity.User</c>).</summary>
    public Guid GuardianUserId { get; set; }

    public required string GuardianName { get; set; }

    public string? GuardianPhone { get; set; }

    /// <summary>Bevestigd via de e-mailverificatie van de aanmelding of door het bestuur.</summary>
    public DateTime VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
