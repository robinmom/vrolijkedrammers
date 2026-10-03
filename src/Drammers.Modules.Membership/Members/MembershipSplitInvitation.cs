namespace Drammers.Modules.Membership.Members;

/// <summary>
/// Uitnodiging aan een hoofdlid om het tweede lid van zijn tweepersoonslidmaatschap te registreren (fase 25). Eén per
/// hoofdlid; opnieuw sturen geeft een nieuwe link. Alleen de hash van de link wordt bewaard.
/// </summary>
public sealed class MembershipSplitInvitation
{
    public Guid Id { get; set; }

    public Guid MemberId { get; set; }

    public required string TokenHash { get; set; }

    public required string SentTo { get; set; }

    public DateTime SentAt { get; set; }

    public Guid? SentBy { get; set; }

    public int TimesSent { get; set; }
}
