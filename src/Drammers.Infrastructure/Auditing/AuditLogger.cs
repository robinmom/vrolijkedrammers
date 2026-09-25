using System.Diagnostics;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Audit.AuditLog;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Auditing;

/// <summary>
/// Schrijft auditregels met hash-keten. Een applicatielock (<c>sp_getapplock</c>) binnen de transactie zorgt dat
/// gelijktijdige schrijvers de keten niet splitsen. Let op: <c>SaveChanges</c> slaat ook openstaande wijzigingen
/// van dezelfde context op, in dezelfde transactie; dat is de bedoeling (wijziging en auditregel samen).
/// </summary>
internal sealed class AuditLogger(DrammersDbContext db, IClock clock, ICurrentActor actor) : IAuditLogger
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var ownTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await db.Database.ExecuteSqlRawAsync(
            """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = 'audit-chain', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Lock op de auditketen niet verkregen', 1;
            """,
            cancellationToken);

        var previousHash = await db.AuditLog
            .OrderByDescending(a => a.Id)
            .Select(a => a.Hash)
            .FirstOrDefaultAsync(cancellationToken);

        var now = clock.UtcNow.UtcDateTime;
        var row = new AuditLogEntry
        {
            // Afgerond op milliseconden: datetime2(3) slaat niet meer op, en de hash moet reproduceerbaar zijn.
            OccurredAt = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc),
            ActorUserId = actor.UserId,
            ActorType = actor.Type,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            OldValues = entry.OldValues,
            NewValues = entry.NewValues,
            CorrelationId = Activity.Current?.TraceId.ToString(),
            HashPrev = previousHash,
            Hash = string.Empty,
        };
        row.Hash = AuditChain.ComputeHash(row);

        db.AuditLog.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(cancellationToken);
        }
    }
}
