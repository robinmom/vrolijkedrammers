namespace Drammers.SharedKernel.Auditing;

/// <summary>De uitvoerder van de huidige request of job; vanaf fase 3 gevuld uit het token.</summary>
public interface ICurrentActor
{
    Guid? UserId { get; }

    ActorType Type { get; }
}
