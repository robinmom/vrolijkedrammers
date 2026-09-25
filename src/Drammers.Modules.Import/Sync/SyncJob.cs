namespace Drammers.Modules.Import.Sync;

public enum SyncJobStatus
{
    Queued,
    Running,
    Succeeded,
    SucceededWithWarnings,
    Conflict,
    Failed,
}

public enum SyncTrigger
{
    Scheduled,
    Manual,
}

/// <summary>Eén run van de ledensync met e-Boekhouden (ADR-010), inclusief dry-runs.</summary>
public sealed class SyncJob
{
    public Guid Id { get; set; }

    public SyncJobStatus Status { get; set; }

    public bool DryRun { get; set; }

    public SyncTrigger Trigger { get; set; }

    public Guid? RequestedBy { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int TotalInSource { get; set; }

    public int Created { get; set; }

    public int Updated { get; set; }

    public int Unchanged { get; set; }

    public int Missing { get; set; }

    public int Deactivated { get; set; }

    public int Reactivated { get; set; }

    public int Warnings { get; set; }

    public int Errors { get; set; }

    public int Conflicts { get; set; }

    /// <summary>Korte foutmelding zonder persoonsgegevens (bijv. "e-Boekhouden niet bereikbaar").</summary>
    public string? ErrorMessage { get; set; }
}

public enum SyncItemAction
{
    Created,
    Updated,
    Unchanged,
    Missing,
    Deactivated,
    Reactivated,
    Warning,
    Error,
    Conflict,
}

/// <summary>Resultaat per lid binnen een run; bevat lidnummers en veldnamen, nooit veldwaarden (ADR-010).</summary>
public sealed class SyncJobItem
{
    public long Id { get; set; }

    public Guid SyncJobId { get; set; }

    public required string MemberNumber { get; set; }

    public Guid? MemberId { get; set; }

    public SyncItemAction Action { get; set; }

    /// <summary>Kommagescheiden veldnamen, bijv. "email,city".</summary>
    public string? ChangedFields { get; set; }

    public string? Message { get; set; }
}

public enum SyncConflictType
{
    DuplicateMemberNumber,
    MemberNumberChanged,
    EmailChangedForActiveAccount,
    MassDeletionGuard,
}

public enum SyncConflictStatus
{
    Open,
    Accepted,
    Ignored,
}

/// <summary>Conflict dat een mens moet beoordelen (ADR-010 §Conflictoplossing).</summary>
public sealed class SyncConflict
{
    public Guid Id { get; set; }

    public Guid SyncJobId { get; set; }

    public SyncConflictType Type { get; set; }

    public string? MemberNumber { get; set; }

    public Guid? MemberId { get; set; }

    /// <summary>Uitleg voor het bestuur, zonder veldwaarden van persoonsgegevens.</summary>
    public required string Details { get; set; }

    public SyncConflictStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? ResolvedBy { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public string? ResolutionNote { get; set; }
}
