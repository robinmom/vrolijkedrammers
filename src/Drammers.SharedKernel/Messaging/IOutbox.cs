namespace Drammers.SharedKernel.Messaging;

/// <summary>
/// Zet een bericht (push, e-mail, …) in de outbox. Het bericht wordt opgeslagen in dezelfde transactie als de
/// wijziging en na de commit door de worker verwerkt (docs/04 §6, ADR-007).
/// </summary>
public interface IOutbox
{
    void Enqueue(string type, object payload);
}
