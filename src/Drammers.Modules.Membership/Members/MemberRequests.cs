namespace Drammers.Modules.Membership.Members;

public enum MemberRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// Een lid wil zijn gegevens wijzigen vanuit de app (fase 26): adres, e-mail, telefoon en/of IBAN. Pas na goedkeuring
/// door de ledenadministratie worden ze doorgevoerd. Een leeg veld betekent "ongewijzigd". De IBAN staat versleuteld.
/// </summary>
public sealed class MemberChangeRequest
{
    public Guid Id { get; set; }

    public Guid MemberId { get; set; }

    public Guid? RequestedBy { get; set; }

    public DateTime RequestedAt { get; set; }

    public MemberRequestStatus Status { get; set; }

    public string? AddressLine { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? MobilePhone { get; set; }

    public string? IbanProtected { get; set; }

    public string? IbanLast4 { get; set; }

    public string? AccountHolder { get; set; }

    public DateTime? MandateConsentAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public Guid? DecidedBy { get; set; }

    public string? RejectionReason { get; set; }
}

public enum CombinationBreakStatus
{
    /// <summary>Eén van beiden heeft het gevraagd; wacht op het akkoord van de ander.</summary>
    AwaitingAgreement,

    /// <summary>Beiden akkoord; wacht op de ledenadministratie.</summary>
    AwaitingApproval,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// Een combinatie (tweepersoonslidmaatschap) verbreken (fase 26): beide leden geven akkoord in de app, daarna keurt de
/// ledenadministratie het goed. Het tweede lid gaat zelf betalen en geeft daarvoor bij zijn akkoord een IBAN en
/// machtiging (versleuteld). Na goedkeuring betalen beiden het tarief voor één lid; de jaren lid blijven gelijk.
/// </summary>
public sealed class CombinationBreakRequest
{
    public Guid Id { get; set; }

    public Guid PayerMemberId { get; set; }

    public Guid PartnerMemberId { get; set; }

    public Guid InitiatedByMemberId { get; set; }

    public DateTime InitiatedAt { get; set; }

    public DateTime? PayerAgreedAt { get; set; }

    public DateTime? PartnerAgreedAt { get; set; }

    public string? PartnerIbanProtected { get; set; }

    public string? PartnerIbanLast4 { get; set; }

    public string? PartnerAccountHolder { get; set; }

    public DateTime? PartnerMandateConsentAt { get; set; }

    public CombinationBreakStatus Status { get; set; }

    public DateTime? DecidedAt { get; set; }

    public Guid? DecidedBy { get; set; }

    public string? RejectionReason { get; set; }
}
