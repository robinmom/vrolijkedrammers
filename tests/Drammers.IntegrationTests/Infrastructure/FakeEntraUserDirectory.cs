using System.Collections.Concurrent;
using Drammers.Infrastructure.Identity.Entra;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>Graph-mock: houdt accounts en aanroepen bij.</summary>
public sealed class FakeEntraUserDirectory : IEntraUserDirectory
{
    public ConcurrentDictionary<string, string> AccountsByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, bool> Enabled { get; } = new();

    public ConcurrentBag<string> RevokedSessions { get; } = [];

    public int CreateCalls;

    public Task<string?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(AccountsByEmail.TryGetValue(email, out var id) ? id : null);

    public Task<string> CreateAsync(string email, string displayName, CancellationToken cancellationToken)
    {
        if (FailNextCreate)
        {
            FailNextCreate = false;
            throw new HttpRequestException("Graph tijdelijk niet bereikbaar");
        }

        Interlocked.Increment(ref CreateCalls);
        var id = Guid.NewGuid().ToString();
        AccountsByEmail[email] = id;
        Enabled[id] = true;
        return Task.FromResult(id);
    }

    public Task SetAccountEnabledAsync(string objectId, bool enabled, CancellationToken cancellationToken)
    {
        Enabled[objectId] = enabled;
        return Task.CompletedTask;
    }

    public Task RevokeSessionsAsync(string objectId, CancellationToken cancellationToken)
    {
        RevokedSessions.Add(objectId);
        return Task.CompletedTask;
    }

    public ConcurrentBag<string> Deleted { get; } = [];

    /// <summary>Laat de volgende <see cref="CreateAsync"/> falen (saga-hervatting testen).</summary>
    public bool FailNextCreate;

    public Task DeleteAsync(string objectId, CancellationToken cancellationToken)
    {
        Deleted.Add(objectId);
        foreach (var entry in AccountsByEmail.Where(e => e.Value == objectId).ToList())
        {
            AccountsByEmail.TryRemove(entry.Key, out _);
        }

        return Task.CompletedTask;
    }
}
