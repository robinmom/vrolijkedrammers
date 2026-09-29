namespace Drammers.Modules.Ticketing.Sales;

public enum WaitlistStatus
{
    Waiting,

    /// <summary>Uitgenodigd: de bestelling staat klaar en moet binnen 48 uur betaald worden.</summary>
    Invited,

    /// <summary>Plaatsen gekregen (betaald, gratis of door het bestuur toegekend).</summary>
    Granted,

    /// <summary>De uitnodiging is verlopen; de volgende schuift op.</summary>
    Expired,

    Withdrawn,
}

/// <summary>
/// Wachtlijst van een product dat vol is (fase 19, vooral de vrijdag van de pronkzitting). Uitnodigen gaat op
/// volgorde van aanmelden, maar het bestuur kan ook zelf toekennen, buiten de volgorde (bijv. een kleine groep die nog
/// past). De groepskaarten worden niet vastgehouden: ook een groep met ruimte binnen het maximum komt hier als het vol is.
/// </summary>
public sealed class WaitlistEntry
{
    public const int InviteHours = 48;

    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public WaitlistStatus Status { get; set; }

    public SaleChannel Channel { get; set; }

    public string? GroupName { get; set; }

    public int MemberQuantity { get; set; }

    public int PaidQuantity { get; set; }

    public required string BuyerName { get; set; }

    public required string BuyerEmail { get; set; }

    public string? BuyerPhone { get; set; }

    public string? Remark { get; set; }

    public Guid? BuyerUserId { get; set; }

    public Guid? BuyerMemberId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? InvitedAt { get; set; }

    public Guid? OrderId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public int Quantity => MemberQuantity + PaidQuantity;
}
