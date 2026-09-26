using System.Collections.Concurrent;
using Drammers.Infrastructure.Identity.Entra;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>Graph-mock: houdt inlogaccounts en aanroepen bij.</summary>
public sealed class FakeEntraUserDirectory : IEntraUserDirectory
{
    public ConcurrentDictionary<string, string> AccountsByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, bool> Enabled { get; } = new();

    public ConcurrentBag<string> RevokedSessions { get; } = [];

    public ConcurrentBag<string> Deleted { get; } = [];

    /// <summary>Iemand maakt zelf een inlog met e-mail + code (zelfregistratie in de user flow); geeft de <c>oid</c>.</summary>
    public string SignUp(string email)
    {
        var id = Guid.NewGuid().ToString();
        AccountsByEmail[email] = id;
        Enabled[id] = true;
        return id;
    }

    public Task<string?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(AccountsByEmail.TryGetValue(email, out var id) ? id : null);

    public Task<string?> GetSignInEmailAsync(string objectId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(AccountsByEmail.FirstOrDefault(e => e.Value == objectId).Key);

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
