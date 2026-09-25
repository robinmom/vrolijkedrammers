using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Drammers.Worker.Outbox;

/// <summary>Pollt de outbox en geeft berichten aan de handler van hun type (ADR-007, docs/04 §6).</summary>
public sealed partial class OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger) : BackgroundService
{
    public const int BatchSize = 10;
    public const int MaxAttempts = 5;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    await ProcessBatchAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogBatchFailed(logger, ex);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Netjes stoppen; geclaimde maar niet afgeronde berichten komen na het verlopen van de lock terug.
        }
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox-batch mislukt; volgende poll opnieuw")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox-bericht {MessageId} ({Type}) mislukt bij poging {Attempts}")]
    private static partial void LogMessageFailed(ILogger logger, Exception exception, Guid messageId, string type, int attempts);
}
