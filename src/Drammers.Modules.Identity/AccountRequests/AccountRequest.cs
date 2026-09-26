namespace Drammers.Modules.Identity.AccountRequests;

public enum AccountRequestStatus
{
    /// <summary>Geen exacte match: wacht op het bestuur.</summary>
    Pending,

    /// <summary>Exacte match of goedgekeurd: het account wordt (of is) aangemaakt.</summary>
    Approved,

    Rejected,

    /// <summary>Het lid had al een account; er is niets aangemaakt.</summary>
    Duplicate,
}

/// <summary>
/// "Ik ben al lid": een bestaand lid vraagt een account aan met lidnummer en e-mailadres (ADR-014). De aanvrager krijgt
/// altijd dezelfde melding; alleen bij een exacte match met e-Boekhouden volgt automatisch een account.
/// </summary>
public sealed class AccountRequest
{
    public Guid Id { get; set; }

    /// <summary>Zoals ingevuld (genormaliseerd), voor de beoordeling door het bestuur.</summary>
    public required string MemberNumber { get; set; }

    public required string Email { get; set; }

    public AccountRequestStatus Status { get; set; }

    /// <summary>Het lid waaraan het account wordt gekoppeld (bij een match of na goedkeuring).</summary>
    public Guid? MemberId { get; set; }

    /// <summary>Waarom er geen automatische match was (voor het bestuur, zonder te zeggen wat wél klopte).</summary>
    public string? MismatchReason { get; set; }

    public string? RejectionReason { get; set; }

    public string? IpHash { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public Guid? DecidedBy { get; set; }
}
