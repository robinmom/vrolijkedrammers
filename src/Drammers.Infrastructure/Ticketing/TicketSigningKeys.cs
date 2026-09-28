using System.Security.Cryptography;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Ticketing;

/// <summary>
/// Sleutel waarmee de server QR-codes ondertekent voor toestellen zonder hardwaresleutel (ADR-005 optie 4, OQ-68).
/// Bij het eerste gebruik maakt de API een ECDSA-P-256-sleutel; de private sleutel staat alleen versleuteld (Data
/// Protection, beschermd door Key Vault) in de database. Geen extra stappen in Azure nodig.
/// </summary>
public sealed class TicketSigningKeys(DrammersDbContext db, IDataProtectionProvider provider, IClock clock)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Drammers.TicketSigningKey.v1");

    public async Task<ECDsa> ActivePrivateKeyAsync(CancellationToken cancellationToken)
    {
        var key = await db.TicketSigningKeys.AsNoTracking().Where(k => k.Active).OrderByDescending(k => k.Id).FirstOrDefaultAsync(cancellationToken)
            ?? await CreateAsync(cancellationToken);
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(_protector.Unprotect(key.ProtectedPrivateKey)), out _);
        return ecdsa;
    }

    /// <summary>Publieke sleutel(s) die een scanner vertrouwt: de actieve sleutel.</summary>
    public async Task<IReadOnlyList<byte[]>> PublicKeysAsync(CancellationToken cancellationToken) =>
        await db.TicketSigningKeys.AsNoTracking().Where(k => k.Active).Select(k => k.PublicKey).ToListAsync(cancellationToken);

    private async Task<TicketSigningKey> CreateAsync(CancellationToken cancellationToken)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new TicketSigningKey
        {
            PublicKey = ecdsa.ExportSubjectPublicKeyInfo(),
            ProtectedPrivateKey = _protector.Protect(Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey())),
            Active = true,
            CreatedAt = clock.UtcNow.UtcDateTime,
        };
        db.TicketSigningKeys.Add(key);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return key;
        }
        catch (DbUpdateException)
        {
            // Een andere instantie was net eerder (unieke index op de actieve sleutel): die sleutel gebruiken.
            db.ChangeTracker.Clear();
            return await db.TicketSigningKeys.AsNoTracking().Where(k => k.Active).OrderByDescending(k => k.Id).FirstAsync(cancellationToken);
        }
    }
}
