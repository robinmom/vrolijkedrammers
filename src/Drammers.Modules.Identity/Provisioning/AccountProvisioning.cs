namespace Drammers.Modules.Identity.Provisioning;

public enum ProvisioningSourceType
{
    MembershipApplication,
    AccountRequest,
    Guardian,
    Manual,
}

public enum ProvisioningKind
{
    Member,
    Guardian,
    Administrator,
}

public enum ProvisioningStep
{
    Pending,
    EbCreated,
    MemberCreated,
    AccountCreated,
    WelcomeSent,
    Completed,
    Failed,
}

/// <summary>
/// Idempotente saga voor het aanmaken van een account (ADR-014). Elke stap wordt vastgelegd, zodat een herhaalde of
/// hervatte aanroep geen tweede e-Boekhouden-lid of Entra-account maakt.
/// </summary>
public sealed class AccountProvisioning
{
    public Guid Id { get; set; }

    public ProvisioningSourceType SourceType { get; set; }

    /// <summary>Bij <see cref="ProvisioningSourceType.Manual"/>: het (genormaliseerde) e-mailadres.</summary>
    public required string SourceId { get; set; }

    public ProvisioningKind Kind { get; set; }

    public ProvisioningStep Step { get; set; }

    public string? EbMemberId { get; set; }

    public string? MemberNumber { get; set; }

    public Guid? MemberId { get; set; }

    public string? EntraObjectId { get; set; }

    public Guid? UserId { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
