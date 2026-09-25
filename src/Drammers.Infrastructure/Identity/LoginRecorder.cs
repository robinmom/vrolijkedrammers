using System.Security.Cryptography;
using System.Text;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Drammers.Infrastructure.Identity;

/// <summary>
/// Legt aanmeldingen vast in <c>identity.LoginHistory</c> (docs/04 §3): één regel per token (niet per request),
/// met gehashte IP-adressen en <c>oid</c>'s.
/// </summary>
public interface ILoginRecorder
{
    Task RecordAsync(string tokenId, Guid? userId, string externalObjectId, LoginResult result, string? reason, string? ip, string? userAgent, CancellationToken cancellationToken);
}

internal sealed class LoginRecorder(DrammersDbContext db, IMemoryCache cache, IClock clock) : ILoginRecorder
{
    public async Task RecordAsync(
        string tokenId, Guid? userId, string externalObjectId, LoginResult result, string? reason, string? ip, string? userAgent, CancellationToken cancellationToken)
    {
        var key = $"login-recorded:{tokenId}:{result}";
        if (cache.TryGetValue(key, out _))
        {
            return;
        }

        cache.Set(key, true, TimeSpan.FromHours(1));
        var now = clock.UtcNow.UtcDateTime;
        db.LoginHistory.Add(new LoginHistory
        {
            UserId = userId,
            SubjectHash = userId is null ? Hash(externalObjectId) : null,
            OccurredAt = now,
            Result = result,
            Reason = reason,
            IpHash = ip is null ? null : Hash(ip),
            UserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent,
        });
        await db.SaveChangesAsync(cancellationToken);

        if (userId is { } id && result == LoginResult.Success)
        {
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, now), cancellationToken);
        }
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
