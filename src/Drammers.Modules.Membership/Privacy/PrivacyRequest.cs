namespace Drammers.Modules.Membership.Privacy;

public enum PrivacyRequestType
{
    Export,
    Erasure,
}

public enum PrivacyRequestStatus
{
    Requested,
    Completed,
    Failed,
}

/// <summary>AVG-verzoek (docs/04 §4, fase 9). Een export is 24 uur te downloaden via een kortlevende link.</summary>
public sealed class PrivacyRequest
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? MemberId { get; set; }

    public PrivacyRequestType Type { get; set; }

    public PrivacyRequestStatus Status { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>Pad in de container <c>exports</c>; na <see cref="ExpiresAt"/> verwijderd.</summary>
    public string? FilePath { get; set; }

    public DateTime? ExpiresAt { get; set; }
}
