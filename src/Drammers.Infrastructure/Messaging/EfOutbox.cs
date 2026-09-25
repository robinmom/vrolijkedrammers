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

    public void Enqueue(string type, object payload) =>
        db.Outbox.Add(new OutboxMessage
        {
            Id = IdGenerator.NewId(),
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAt = clock.UtcNow.UtcDateTime,
        });
}
