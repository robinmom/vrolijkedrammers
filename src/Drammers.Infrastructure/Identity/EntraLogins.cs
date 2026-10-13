using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Identity;

/// <summary>
/// Bepaalt bij welke inlog (Entra <c>oid</c>) een account met dit e-mailadres hoort. Eén e-mailadres = één account: een
/// bestaand account met dit adres gaat voor, daarna een bestaande inlog bij Entra. Met inloggen met wachtwoord
/// (<see cref="AccountOptions.PasswordSignIn"/>) maakt de API de inlog meteen zelf; anders wacht het account op de eerste
/// aanmelding van het lid (<see cref="PendingObjectId"/>, gekoppeld door <see cref="AccountLinker"/>).
/// </summary>
public sealed partial class EntraLogins(
    DrammersDbContext db, IEntraUserDirectory entra, IOptions<AccountOptions> options, IUserAccessService userAccess, IAuditLogger audit,
    ILogger<EntraLogins> logger)
{
    public bool PasswordSignIn => options.Value.PasswordSignIn;

    public async Task<string> ResolveAsync(string email, string displayName, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var local = await db.Users.AsNoTracking()
            .Where(u => u.Email == normalized && u.AccountStatus != AccountStatus.Deleted)
            .Select(u => new { u.Id, u.ExternalObjectId })
            .FirstOrDefaultAsync(cancellationToken);
        if (local is not null && !PendingObjectId.IsPending(local.ExternalObjectId))
        {
            return PasswordSignIn
                ? await EnsurePasswordAsync(local.ExternalObjectId, normalized, displayName, cancellationToken)
                : local.ExternalObjectId;
        }

        var objectId = await entra.FindByEmailAsync(normalized, cancellationToken);
        if (objectId is null)
        {
            if (!PasswordSignIn)
            {
                return local?.ExternalObjectId ?? PendingObjectId.New();
            }

            objectId = await entra.CreatePasswordAccountAsync(normalized, displayName, cancellationToken);
        }
        else if (PasswordSignIn)
        {
            objectId = await EnsurePasswordAsync(objectId, normalized, displayName, cancellationToken);
        }

        // Een account dat nog op de eerste aanmelding wachtte, krijgt nu de inlog (geen tweede account).
        if (local is not null && !await db.Users.AnyAsync(u => u.ExternalObjectId == objectId, cancellationToken))
        {
            await db.Users.Where(u => u.Id == local.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalObjectId, objectId), cancellationToken);
        }

        return objectId;
    }

    /// <summary>
    /// Zorgt voor een wachtwoordinlog op deze inlog. Een inlog met alleen e-mail + code kan dat niet krijgen (Graph:
    /// "recreate the user"): dan een nieuwe inlog met wachtwoord, het account (rollen, kinderen, QR) gaat mee naar de nieuwe
    /// <c>oid</c> en de oude inlog wordt verwijderd. Testtoegang (Dev/Acc) gaat mee.
    /// </summary>
    private async Task<string> EnsurePasswordAsync(string objectId, string email, string displayName, CancellationToken cancellationToken)
    {
        if (await entra.EnsurePasswordSignInAsync(objectId, email, cancellationToken))
        {
            return objectId;
        }

        EntraTestAccess? access = null;
        try
        {
            access = await entra.GetTestAccessAsync(objectId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Testtoegang niet geconfigureerd (Prod): niets mee te nemen.
        }

        string newObjectId;
        var oldDeleted = false;
        try
        {
            newObjectId = await entra.CreatePasswordAccountAsync(email, displayName, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Mocht Graph het adres als bezet zien zolang de oude inlog bestaat: eerst de oude weg, dan opnieuw.
            LogRecreateRetry(logger, objectId, ex);
            await entra.DeleteAsync(objectId, cancellationToken);
            oldDeleted = true;
            newObjectId = await entra.CreatePasswordAccountAsync(email, displayName, cancellationToken);
        }

        if (access is { Environments: { } environments })
        {
            await entra.SetTestAccessAsync(newObjectId, environments, cancellationToken);
        }

        await db.Users.Where(u => u.ExternalObjectId == objectId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalObjectId, newObjectId), cancellationToken);
        userAccess.Invalidate(objectId);
        if (!oldDeleted)
        {
            await entra.DeleteAsync(objectId, cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("user.login-recreated-with-password", "User", newObjectId, objectId, null), cancellationToken);
        return newObjectId;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nieuwe wachtwoordinlog naast inlog {ObjectId} lukte niet; oude inlog eerst verwijderd")]
    private static partial void LogRecreateRetry(ILogger logger, string objectId, Exception exception);
}
