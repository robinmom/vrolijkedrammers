using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Drammers.Worker.Outbox;

/// <summary>
/// Verwerkt de outbox (ADR-007, docs/04 §6) zonder te pollen: de worker slaapt tot het eerstvolgende bericht verwerkt
/// kan worden (nieuwe poging, gepland moment) of tot <see cref="OutboxSignal"/> meldt dat er een bericht is opgeslagen.
/// Zonder werk raakt de worker de database dus niet aan, zodat een serverless database kan pauzeren (Dev: gratis
/// tegoed).
/// </summary>
public sealed partial class OutboxProcessor(
    IServiceScopeFactory scopeFactory, OutboxSignal signal, TimeProvider time, ILogger<OutboxProcessor> logger) : BackgroundService
{
    public const int BatchSize = 10;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    /// <summary>Na een signaal even wachten: het signaal kan vlak vóór de commit komen.</summary>
    public static readonly TimeSpan SignalSettleDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Na een databasefout niet meteen opnieuw (de database kan gepauzeerd of onbereikbaar zijn).</summary>
    public static readonly TimeSpan ErrorBackoff = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan wait;
                try
                {
                    while (await ProcessBatchAsync(stoppingToken) == BatchSize)
                    {
                        // Volle batch: meteen de volgende.
                    }

                    wait = await TimeUntilNextDueAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogBatchFailed(logger, ex);
                    wait = ErrorBackoff;
                }

                if (await signal.WaitAsync(wait, stoppingToken))
                {
                    await Task.Delay(SignalSettleDelay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Netjes stoppen; geclaimde maar niet afgeronde berichten komen na het verlopen van de lock terug.
        }
    }

    /// <summary>Wachttijd tot het eerstvolgende bericht; oneindig als er niets openstaat (dan alleen een signaal).</summary>
    public async Task<TimeSpan> TimeUntilNextDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var next = await scope.ServiceProvider.GetRequiredService<IOutboxStore>().NextDueAsync(cancellationToken);
        if (next is not { } due)
        {
            return Timeout.InfiniteTimeSpan;
        }

        var wait = due - time.GetUtcNow().UtcDateTime;
        // Minimaal een seconde (klokverschil database/app), maximaal een dag per slaapronde.
        return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : wait;
    }

    /// <summary>Verwerkt één batch; geeft het aantal verwerkte berichten terug. Publiek voor tests.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var handlers = scope.ServiceProvider.GetServices<IOutboxMessageHandler>().ToDictionary(h => h.Type);

        var messages = await store.ClaimAsync(BatchSize, LockDuration, cancellationToken);
        foreach (var message in messages)
        {
            if (!handlers.TryGetValue(message.Type, out var handler))
            {
                await store.MarkFailedAsync(message.Id, $"Geen handler voor type '{message.Type}'", RetryDelay(message.Attempts), cancellationToken);
                continue;
            }

            try
            {
                await handler.HandleAsync(message, cancellationToken);
                await store.MarkProcessedAsync(message.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogMessageFailed(logger, ex, message.Id, message.Type, message.Attempts);
                await store.MarkFailedAsync(message.Id, ex.Message, RetryDelay(message.Attempts), cancellationToken);
            }
        }

        return messages.Count;
    }

    /// <summary>Exponentieel uitstel: 30 s, 1 min, 2 min, … (maximaal 1 uur).</summary>
    public static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Max(0, attempts - 1))));

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox-batch mislukt; over enkele minuten opnieuw")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox-bericht {MessageId} ({Type}) mislukt bij poging {Attempts}")]
    private static partial void LogMessageFailed(ILogger logger, Exception exception, Guid messageId, string type, int attempts);
}
