using Drammers.SharedKernel.Auditing;

namespace Drammers.Modules.Audit.AuditLog;

/// <summary>Append-only auditregel met hash-keten (docs/04 §11). Tabel <c>audit.AuditLog</c>.</summary>
public sealed class AuditLogEntry
{
    public long Id { get; set; }

    public DateTime OccurredAt { get; set; }

    public Guid? ActorUserId { get; set; }

    public ActorType ActorType { get; set; }

    public required string Action { get; set; }

    public required string EntityType { get; set; }

    public required string EntityId { get; set; }

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    public string? IpHash { get; set; }

    public Guid? DeviceId { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>Hash van de vorige regel; <c>null</c> voor de eerste regel.</summary>
    public string? HashPrev { get; set; }

    public required string Hash { get; set; }
}
