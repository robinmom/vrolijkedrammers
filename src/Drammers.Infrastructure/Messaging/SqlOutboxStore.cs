using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Messaging;

/// <summary>
/// Claimt outbox-berichten met <c>UPDATE … OUTPUT</c> en <c>READPAST</c>: gelijktijdige instanties slaan elkaars
/// geclaimde rijen over. Een claim verloopt na de lockduur, zodat berichten na een crash terugkomen (ADR-007).
/// </summary>
internal sealed class SqlOutboxStore(DrammersDbContext db, IClock clock) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxEnvelope>> ClaimAsync(int batchSize, TimeSpan lockDuration, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var lockedUntil = now.Add(lockDuration);
        var rows = await db.Database.SqlQuery<ClaimedRow>(
            $"""
            WITH next AS (
                SELECT TOP ({batchSize}) *
                FROM notification.Outbox WITH (ROWLOCK, READPAST, UPDLOCK)
                WHERE processed_at IS NULL
                  AND attempts < {OutboxProcessor.MaxAttempts}
                  AND (locked_until IS NULL OR locked_until < {now})
                ORDER BY created_at)
            UPDATE next
            SET locked_until = {lockedUntil}, attempts = attempts + 1
            OUTPUT inserted.id AS Id, inserted.type AS Type, inserted.payload AS Payload, inserted.attempts AS Attempts;
            """).ToListAsync(cancellationToken);

        return [.. rows.Select(r => new OutboxEnvelope(r.Id, r.Type, r.Payload, r.Attempts))];
    }

    public Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken) =>
        db.Outbox.Where(m => m.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(m => m.ProcessedAt, clock.UtcNow.UtcDateTime)
                .SetProperty(m => m.LockedUntil, (DateTime?)null)
                .SetProperty(m => m.LastError, (string?)null),
            cancellationToken);

    public Task MarkFailedAsync(Guid id, string error, TimeSpan retryAfter, CancellationToken cancellationToken) =>
        db.Outbox.Where(m => m.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(m => m.LockedUntil, clock.UtcNow.UtcDateTime.Add(retryAfter))
                .SetProperty(m => m.LastError, error.Length > 2000 ? error[..2000] : error),
            cancellationToken);

    private sealed class ClaimedRow
    {
        public Guid Id { get; set; }

        public string Type { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public int Attempts { get; set; }
    }
}
