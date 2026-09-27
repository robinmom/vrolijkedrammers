using System.Data.Common;
using Drammers.Modules.Notification.Outbox;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Drammers.Infrastructure.Messaging;

/// <summary>
/// Meldt de outbox-worker dat er een bericht klaarstaat, pas als het ook echt zichtbaar is: direct na <c>SaveChanges</c>
/// zonder transactie, anders na de commit. Eén instantie per <c>DbContext</c> (scoped).
/// </summary>
internal sealed class OutboxSignalInterceptor(OutboxSignal signal) : DbTransactionInterceptor, ISaveChangesInterceptor
{
    private bool _pending;

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Detect(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Detect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SignalIfCommitted(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        SignalIfCommitted(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Flush();

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Flush();
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _pending = false;

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = false;
        return Task.CompletedTask;
    }

    private void Detect(DbContext? context) =>
        _pending |= context?.ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added) == true;

    private void SignalIfCommitted(DbContext? context)
    {
        if (context?.Database.CurrentTransaction is null)
        {
            Flush();
        }
    }

    private void Flush()
    {
        if (_pending)
        {
            _pending = false;
            signal.Notify();
        }
    }
}
