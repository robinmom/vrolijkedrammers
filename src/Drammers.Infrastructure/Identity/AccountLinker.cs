using System.Text.Json;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Drammers.Infrastructure.Identity;

/// <summary>Plaatshouder voor de <c>oid</c> van een goedgekeurd account waarvoor nog niemand heeft ingelogd.</summary>
public static class PendingObjectId
{
    public const string Prefix = "pending:";

    public static string New() => Prefix + Guid.NewGuid().ToString("N");

    public static bool IsPending(string objectId) => objectId.StartsWith(Prefix, StringComparison.Ordinal);
}

/// <summary>
/// Koppelt een inlogaccount (e-mail + eenmalige code, zelf aangemaakt) aan een account bij de vereniging (ADR-014,
/// herzien 2026-09-27). Alleen als het geverifieerde e-mailadres precies één goedgekeurd account oplevert:
/// <list type="bullet">
/// <item>nog nooit ingelogd (<see cref="PendingObjectId"/>) → koppelen;</item>
/// <item>gekoppeld aan een inlogaccount dat niet meer bestaat (bijv. het oude account met wachtwoord) → opnieuw koppelen.</item>
/// </list>
/// Iemand die zich zonder goedkeuring aanmeldt, blijft onbekend (403); rollen en gegevens komen nooit uit Entra.
/// </summary>
public sealed partial class AccountLinker(
    DrammersDbContext db,
    IEntraUserDirectory entra,
    IUserAccessService userAccess,
    IAuditLogger audit,
    IMemoryCache cache,
    ILogger<AccountLinker> logger)
{
    /// <summary>Hoe lang een onbekende <c>oid</c> niet opnieuw in Graph wordt opgezocht.</summary>
    public static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <returns><c>true</c> als de <c>oid</c> nu aan een account is gekoppeld.</returns>
    public async Task<bool> TryLinkAsync(string objectId, CancellationToken cancellationToken)
    {
        var cacheKey = $"account-link-miss:{objectId}";
        if (PendingObjectId.IsPending(objectId) || cache.TryGetValue(cacheKey, out _))
        {
            return false;
        }

        try
        {
            var linked = await LinkAsync(objectId, cancellationToken);
            if (!linked)
            {
                cache.Set(cacheKey, true, NegativeCacheDuration);
            }

            return linked;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // Graph niet bereikbaar of niet geconfigureerd: de aanroep blijft 403, een volgende poging kan slagen.
            LogLinkFailed(logger, ex);
            return false;
        }
    }

    private async Task<bool> LinkAsync(string objectId, CancellationToken cancellationToken)
    {
        var email = (await entra.GetSignInEmailAsync(objectId, cancellationToken))?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email))
        {
            return false;
        }

        var candidates = await db.Users
            .Where(u => u.Email == email && u.AccountStatus != AccountStatus.Deleted && u.ExternalObjectId != objectId)
            .ToListAsync(cancellationToken);
        if (candidates.Count != 1)
        {
            return false;
        }

        var user = candidates[0];
        var previous = user.ExternalObjectId;
        var relink = !PendingObjectId.IsPending(previous);
        if (relink && await entra.GetSignInEmailAsync(previous, cancellationToken) is not null)
        {
            // Het oude inlogaccount bestaat nog: niet overnemen (twee inlogs voor één e-mailadres zou niet mogen).
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        user.ExternalObjectId = objectId;
        user.PermissionsVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(relink ? "user.relinked" : "user.linked", "User", user.Id.ToString(), null, JsonSerializer.Serialize(new { firstSignIn = !relink }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        userAccess.Invalidate(objectId);
        userAccess.Invalidate(previous);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Koppelen van een nieuw inlogaccount mislukt")]
    private static partial void LogLinkFailed(ILogger logger, Exception exception);
}
