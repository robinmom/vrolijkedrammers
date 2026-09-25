namespace Drammers.Worker.Outbox;

/// <summary>
/// Verwerkt één berichttype. De outbox garandeert dat een bericht niet tegelijk door twee instanties wordt verwerkt
/// en na een crash opnieuw wordt aangeboden; handlers gebruiken <see cref="OutboxEnvelope.Id"/> als idempotency-sleutel
/// bij externe diensten.
/// </summary>
public interface IOutboxMessageHandler
{
    string Type { get; }

    Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}
