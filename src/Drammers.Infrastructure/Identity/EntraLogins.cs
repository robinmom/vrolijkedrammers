using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Identity;

/// <summary>
/// Bepaalt bij welke inlog (Entra <c>oid</c>) een account met dit e-mailadres hoort. Eén e-mailadres = één account: een
/// bestaand account met dit adres gaat voor, daarna een bestaande inlog bij Entra. Met inloggen met wachtwoord
/// (<see cref="AccountOptions.PasswordSignIn"/>) maakt de API de inlog meteen zelf; anders wacht het account op de eerste
/// aanmelding van het lid (<see cref="PendingObjectId"/>, gekoppeld door <see cref="AccountLinker"/>).
/// </summary>
public sealed class EntraLogins(DrammersDbContext db, IEntraUserDirectory entra, IOptions<AccountOptions> options)
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
            if (PasswordSignIn)
            {
                await entra.EnsurePasswordSignInAsync(local.ExternalObjectId, normalized, cancellationToken);
            }

            return local.ExternalObjectId;
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
            await entra.EnsurePasswordSignInAsync(objectId, normalized, cancellationToken);
        }

        // Een account dat nog op de eerste aanmelding wachtte, krijgt nu de inlog (geen tweede account).
        if (local is not null && !await db.Users.AnyAsync(u => u.ExternalObjectId == objectId, cancellationToken))
        {
            await db.Users.Where(u => u.Id == local.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ExternalObjectId, objectId), cancellationToken);
        }

        return objectId;
    }
}
