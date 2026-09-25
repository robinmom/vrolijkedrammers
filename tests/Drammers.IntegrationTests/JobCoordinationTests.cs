using Drammers.IntegrationTests.Infrastructure;
using Drammers.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Drammers.IntegrationTests;

/// <summary>sp_getapplock + ScheduledJob voorkomen dubbele uitvoering bij meerdere instanties (ADR-007).</summary>
[Collection(SqlServerCollection.Name)]
public class JobCoordinationTests(SqlServerFixture sql)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Gelijktijdige_starts_leveren_precies_één_uitvoering()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        await using var provider = TestServices.Create(connectionString, clock);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IJobCoordinator>().TryStartAsync("test-job", Interval, CancellationToken.None);
        }));

        Assert.Single(results, started => started);
    }

    [Fact]
    public async Task Twee_instanties_voeren_de_heartbeat_niet_dubbel_uit()
    {
        var connectionString = await sql.CreateMigratedDatabaseAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var executions = new CountingJob();
        var registration = new RecurringJobRegistration("heartbeat", Interval, typeof(CountingJob));

        // Twee "instanties": elk een eigen containter en scheduler, dezelfde database.
        RecurringJobScheduler CreateInstance(out ServiceProvider provider)
        {
            provider = TestServices.Create(connectionString, clock, s => s.AddSingleton(executions));
            return new RecurringJobScheduler(
                provider.GetRequiredService<IServiceScopeFactory>(), [registration], NullLogger<RecurringJobScheduler>.Instance);
        }

        var instanceA = CreateInstance(out var providerA);
        var instanceB = CreateInstance(out var providerB);
        await using (providerA)
        await using (providerB)
        {
            for (var tick = 0; tick < 5; tick++)
            {
                await Task.WhenAll(
                    instanceA.RunOnceAsync(registration, CancellationToken.None),
                    instanceB.RunOnceAsync(registration, CancellationToken.None));
                clock.Advance(Interval);
            }
        }

        Assert.Equal(5, executions.Count);
    }

    public sealed class CountingJob : IRecurringJob
    {
        private int _count;

        public int Count => _count;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return Task.CompletedTask;
        }
    }
}
