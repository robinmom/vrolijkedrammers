using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Outbox;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;

namespace Drammers.Infrastructure.Messaging;

/// <summary>Voegt het bericht toe aan de context; het wordt opgeslagen bij de volgende <c>SaveChanges</c>.</summary>
internal sealed class EfOutbox(DrammersDbContext db, IClock clock) : IOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // LockedUntil doet dienst als "niet vóór": de worker claimt alleen berichten waarvan dat moment verstreken is.
    public void Enqueue(string type, object payload, DateTime? notBefore = null) =>
        db.Outbox.Add(new OutboxMessage
        {
            Id = IdGenerator.NewId(),
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAt = clock.UtcNow.UtcDateTime,
            LockedUntil = notBefore,
        });
}
