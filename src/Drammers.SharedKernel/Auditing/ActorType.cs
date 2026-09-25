namespace Drammers.SharedKernel.Auditing;

/// <summary>Wie een actie uitvoerde (docs/04 §11).</summary>
public enum ActorType
{
    User,
    System,
    Sync,
    Webhook,
}
