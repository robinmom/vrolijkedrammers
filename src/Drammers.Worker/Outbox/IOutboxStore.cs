namespace Drammers.Worker.Outbox;

/// <summary>Opslag van de outbox; de SQL-implementatie claimt berichten met <c>READPAST</c> (ADR-007).</summary>
public interface IOutboxStore
{
    /// <summary>Claimt maximaal <paramref name="batchSize"/> berichten tot <paramref name="lockDuration"/> verstreken is.</summary>
    Task<IReadOnlyList<OutboxEnvelope>> ClaimAsync(int batchSize, TimeSpan lockDuration, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Geeft het bericht vrij voor een nieuwe poging na <paramref name="retryAfter"/>.</summary>
    Task MarkFailedAsync(Guid id, string error, TimeSpan retryAfter, CancellationToken cancellationToken);
}

/// <summary>Geclaimd outbox-bericht; <c>Attempts</c> is inclusief de huidige poging.</summary>
public sealed record OutboxEnvelope(Guid Id, string Type, string Payload, int Attempts);
