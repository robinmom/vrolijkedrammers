using Drammers.SharedKernel.Auditing;

namespace Drammers.Infrastructure.Auditing;

/// <summary>Standaard-actor tot fase 3: alles wordt vastgelegd als systeemactie.</summary>
internal sealed class SystemActor : ICurrentActor
{
    public Guid? UserId => null;

    public ActorType Type => ActorType.System;
}
