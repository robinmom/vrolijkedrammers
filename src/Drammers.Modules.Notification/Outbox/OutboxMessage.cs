namespace Drammers.Modules.Notification.Outbox;

/// <summary>Bericht in <c>notification.Outbox</c>; verwerkt door de worker na de commit (docs/04 §6).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public required string Type { get; set; }

    /// <summary>JSON.</summary>
    public required string Payload { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    /// <summary>Tot dit tijdstip is het bericht geclaimd door een worker-instantie (of uitgesteld na een fout).</summary>
    public DateTime? LockedUntil { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
