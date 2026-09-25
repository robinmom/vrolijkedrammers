using System.Collections.Concurrent;
using Drammers.Infrastructure.Persistence;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.SharedKernel.Messaging;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Drammers.IntegrationTests;

/// <summary>Elk outbox-bericht wordt precies één keer verwerkt, ook met twee instanties en na een crash (fase 2).</summary>
[Collection(SqlServerCollection.Name)]
public class OutboxTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Twee_processors_verwerken_elk_bericht_precies_één_keer()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var handler = new RecordingHandler();
        await using var provider = TestServices.Create(connectionString, clock, s => s.AddSingleton<IOutboxMessageHandler>(handler));
        await EnqueueAsync(provider, 35);

        var processorA = new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxProcessor>.Instance);
        var processorB = new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxProcessor>.Instance);
        while ((await Task.WhenAll(processorA.ProcessBatchAsync(default), processorB.ProcessBatchAsync(default))).Sum() > 0)
        {
        }

        Assert.Equal(35, handler.Handled.Count);
        Assert.All(handler.Handled.Values, count => Assert.Equal(1, count));
        Assert.Equal(0, await CountUnprocessedAsync(provider));
    }

    [Fact]
    public async Task Bericht_van_een_gecrashte_instantie_wordt_na_de_lock_alsnog_één_keer_verwerkt()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var handler = new RecordingHandler();
        await using var provider = TestServices.Create(connectionString, clock, s => s.AddSingleton<IOutboxMessageHandler>(handler));
        await EnqueueAsync(provider, 1);

        // Instantie A claimt het bericht en "crasht" voordat het verwerkt is.
        await using (var scope = provider.CreateAsyncScope())
        {
            var claimed = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .ClaimAsync(10, OutboxProcessor.LockDuration, default);
            Assert.Single(claimed);
        }

        var processor = new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxProcessor>.Instance);
        Assert.Equal(0, await processor.ProcessBatchAsync(default));

        clock.Advance(OutboxProcessor.LockDuration + TimeSpan.FromSeconds(1));
        Assert.Equal(1, await processor.ProcessBatchAsync(default));
        Assert.Equal(0, await processor.ProcessBatchAsync(default));

        Assert.Equal(1, Assert.Single(handler.Handled).Value);
        await using var check = provider.CreateAsyncScope();
        var message = await check.ServiceProvider.GetRequiredService<DrammersDbContext>().Outbox.SingleAsync();
        Assert.Equal(2, message.Attempts);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task Mislukt_bericht_wordt_later_opnieuw_aangeboden()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var handler = new RecordingHandler { FailFirstAttempt = true };
        await using var provider = TestServices.Create(connectionString, clock, s => s.AddSingleton<IOutboxMessageHandler>(handler));
        await EnqueueAsync(provider, 1);
        var processor = new OutboxProcessor(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxProcessor>.Instance);

        await processor.ProcessBatchAsync(default);
        Assert.Equal(0, await processor.ProcessBatchAsync(default));
        clock.Advance(OutboxProcessor.RetryDelay(1) + TimeSpan.FromSeconds(1));
        await processor.ProcessBatchAsync(default);

        Assert.Equal(1, Assert.Single(handler.Handled).Value);
        Assert.Equal(0, await CountUnprocessedAsync(provider));
    }

    private static async Task EnqueueAsync(ServiceProvider provider, int count)
    {
        await using var scope = provider.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        for (var i = 0; i < count; i++)
        {
            outbox.Enqueue(RecordingHandler.MessageType, new { Number = i });
        }

        await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().SaveChangesAsync();
    }

    private static async Task<int> CountUnprocessedAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().Outbox.CountAsync(m => m.ProcessedAt == null);
    }

    private sealed class RecordingHandler : IOutboxMessageHandler
    {
        public const string MessageType = "test.message";

        public ConcurrentDictionary<Guid, int> Handled { get; } = new();

        public bool FailFirstAttempt { get; init; }

        public string Type => MessageType;

        public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
        {
            if (FailFirstAttempt && message.Attempts == 1)
            {
                throw new InvalidOperationException("Tijdelijke fout");
            }

            Handled.AddOrUpdate(message.Id, 1, (_, count) => count + 1);
            return Task.CompletedTask;
        }
    }
}
