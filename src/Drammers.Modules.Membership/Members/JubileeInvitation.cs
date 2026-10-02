namespace Drammers.Modules.Membership.Members;

/// <summary>Uitnodiging voor de huldiging van een jubilaris (fase 20b); per lid en carnavalsjaar hooguit één.</summary>
public sealed class JubileeInvitation
{
    public Guid Id { get; set; }

    public Guid MemberId { get; set; }

    public int CarnivalYearId { get; set; }

    /// <summary>Aantal jaren lid op het moment van uitnodigen.</summary>
    public int Years { get; set; }

    public required string SentTo { get; set; }

    public DateTime InvitedAt { get; set; }

    public Guid? InvitedBy { get; set; }

    /// <summary>Gezet door de outbox zodra de e-mail is aangeboden.</summary>
    public DateTime? EmailSentAt { get; set; }
}
