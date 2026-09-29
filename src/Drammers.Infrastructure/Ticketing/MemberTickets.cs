using System.Security.Cryptography;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Qr;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Ticketing;

public enum TicketState
{
    /// <summary>Geen (actief) lidmaatschap of geen actief carnavalsjaar.</summary>
    None,
    Blocked,
    NotYetValid,
    Valid,
    Ended,
}

/// <summary>"Mijn QR": alles wat de app nodig heeft om de code te maken (geen persoonsgegevens in de code zelf).</summary>
public sealed record MyTicket(
    TicketState State, string Message, string? HolderName, string? CarnivalYearName, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo,
    string? PublicRef, int CredentialVersion, bool BoundToThisDevice, string? BoundDeviceName, int RebindsLeft, string? DeviceShortId,
    bool DeviceHasHardwareKey, string? AccessTitle = null);

public sealed record ServerCode(string Code, long IssuedAt, int ValidFor);

/// <summary>
/// Ledentickets voor leden (fase 13, ADR-005). Een actief lid krijgt automatisch één ticket per carnavalsjaar (ook
/// na een nieuwe activatie). Toestellen met een hardwaresleutel ondertekenen de QR zelf; zonder hardwaresleutel
/// (of in Expo Go) haalt de app elke 30 seconden een door de server ondertekende code op (fallback, OQ-68).
/// </summary>
public sealed class MemberTickets(DrammersDbContext db, TicketSigningKeys keys, AccessWindows windows, IAuditLogger audit, IClock clock)
{
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);
    public static readonly HashSet<string> HardwareLevels = ["SecureEnclave", "StrongBox", "TrustedEnvironment"];
    public static readonly HashSet<string> SecurityLevels = [.. HardwareLevels, "UnknownSecure", "Software"];

    private static readonly TimeZoneInfo Loil = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private DateTime Now => clock.UtcNow.UtcDateTime;

    /// <summary>
    /// Geldigheid: de hele carnavalsperiode (OQ-20), van de eerste dag 00:00 tot de dag na de laatste 06:00 (Loil),
    /// zodat de laatste avond na middernacht nog telt.
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) Validity(CarnivalYear year)
    {
        var from = year.CarnivalStartDate.ToDateTime(TimeOnly.MinValue);
        var to = year.CarnivalEndDate.AddDays(1).ToDateTime(new TimeOnly(6, 0));
        return (new DateTimeOffset(from, Loil.GetUtcOffset(from)), new DateTimeOffset(to, Loil.GetUtcOffset(to)));
    }

    private async Task<Device> CurrentDeviceAsync(Guid userId, string? installationId, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(installationId)
            ? throw new DomainException(ErrorCodes.Validation, "Dit toestel is niet aangemeld. Log opnieuw in.")
            : await db.Devices.SingleOrDefaultAsync(d => d.UserId == userId && d.InstallationId == installationId && d.Status == DeviceStatus.Active, cancellationToken)
              ?? throw new DomainException(ErrorCodes.Validation, "Dit toestel is niet aangemeld. Log opnieuw in.");

    /// <summary>
    /// Het lid waarvan de gebruiker het ticket toont: het eigen lid, of (fase 17) een kind waarvan de gebruiker
    /// ouder/verzorger is, zolang het kind jonger dan 18 is en geen eigen account heeft.
    /// </summary>
    private async Task<Guid?> HolderAsync(Guid userId, Guid? childMemberId, CancellationToken cancellationToken)
    {
        if (childMemberId is not { } childId)
        {
            return await db.Users.Where(u => u.Id == userId).Select(u => u.MemberId).SingleOrDefaultAsync(cancellationToken);
        }

        var child = await db.GuardianRelations.AsNoTracking().Where(g => g.GuardianUserId == userId && g.MemberId == childId)
            .Join(db.Members, g => g.MemberId, m => m.Id, (g, m) => new { m.Id, m.BirthDate }).SingleOrDefaultAsync(cancellationToken);
        if (child is null || !Members.Guardians.IsMinor(child.BirthDate, DateOnly.FromDateTime(Now)))
        {
            throw new DomainException(ErrorCodes.MemberNotFound, "Kind niet gevonden.", DomainErrorKind.NotFound);
        }

        if (await db.Users.AnyAsync(u => u.MemberId == childId && u.AccountStatus != Modules.Identity.Users.AccountStatus.Deleted, cancellationToken))
        {
            throw new DomainException(ErrorCodes.TicketUnavailable, "Je kind heeft een eigen account; de QR staat op de eigen telefoon.", DomainErrorKind.Conflict);
        }

        return child.Id;
    }

    /// <summary>Zoekt of maakt (idempotent) het ticket van het lid voor het actieve carnavalsjaar.</summary>
    private async Task<(Ticket? Ticket, Member? Member, CarnivalYear? Year)> TicketAsync(
        Guid userId, bool create, CancellationToken cancellationToken, Guid? childMemberId = null)
    {
        var memberId = await HolderAsync(userId, childMemberId, cancellationToken);
        var member = memberId is null ? null : await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken);
        var year = await db.CarnivalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Active, cancellationToken);
        if (member is null || year is null)
        {
            return (null, member, year);
        }

        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.CarnivalYearId == year.Id && t.MemberId == member.Id, cancellationToken);
        if (ticket is null && create && member.MembershipStatus == MembershipStatus.Active)
        {
            ticket = NewTicket(year.Id, member.Id);
            db.Tickets.Add(ticket);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gelijktijdig aangemaakt (unieke index op jaar + lid): het bestaande ticket gebruiken.
                db.ChangeTracker.Clear();
                ticket = await db.Tickets.SingleAsync(t => t.CarnivalYearId == year.Id && t.MemberId == member.Id, cancellationToken);
            }
        }

        return (ticket, member, year);
    }

    internal Ticket NewTicket(int yearId, Guid memberId) => new()
    {
        Id = IdGenerator.NewId(),
        CarnivalYearId = yearId,
        MemberId = memberId,
        PublicRef = RandomNumberGenerator.GetBytes(16),
        CredentialVersion = 1,
        Status = TicketStatus.Active,
        CreatedAt = Now,
    };

    public async Task<MyTicket> GetAsync(Guid userId, string? installationId, CancellationToken cancellationToken, Guid? childMemberId = null)
    {
        var (ticket, member, year) = await TicketAsync(userId, create: true, cancellationToken, childMemberId);
        if (ticket is null || member is null || year is null)
        {
            var message = member is null
                ? "Je account is niet gekoppeld aan een lid; alleen leden hebben een ledenticket."
                : year is null ? "Er is nog geen carnavalsjaar actief." : "Je lidmaatschap is niet actief. Klopt dit niet? Neem contact op met het bestuur.";
            return new MyTicket(TicketState.None, message, member?.FullName, year?.Name, null, null, null, 0, false, null, 0, null, false);
        }

        var device = string.IsNullOrWhiteSpace(installationId)
            ? null
            : await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == userId && d.InstallationId == installationId && d.Status == DeviceStatus.Active, cancellationToken);
        var bound = ticket.BoundDeviceId is { } boundId
            ? await db.Devices.AsNoTracking().Where(d => d.Id == boundId).Select(d => d.Name).SingleOrDefaultAsync(cancellationToken)
            : null;
        var (from, to) = Validity(year);
        var now = clock.UtcNow;
        // QR en scannen volgen dezelfde regels: geldig tijdens carnaval én tijdens een activiteit met toegangscontrole.
        var access = now < from || now > to ? await windows.CurrentAsync(cancellationToken) : null;
        var (state, text) = member.MembershipStatus != MembershipStatus.Active
            ? (TicketState.None, "Je lidmaatschap is niet actief. Klopt dit niet? Neem contact op met het bestuur.")
            : ticket.Status == TicketStatus.Blocked
                ? (TicketState.Blocked, "Je ticket is geblokkeerd. Neem contact op met het bestuur.")
                : access is { EventId: not null } ? (TicketState.Valid, $"Geldig bij {access.Title}.")
                : now < from ? (TicketState.NotYetValid, "Je QR verschijnt bij carnaval of bij een activiteit met toegangscontrole.")
                : now > to ? (TicketState.Ended, "Carnaval is voorbij; je QR verschijnt weer bij een activiteit met toegangscontrole.")
                : (TicketState.Valid, "Geldig ticket.");
        return new MyTicket(
            state, text, member.FullName, year.Name, from, to,
            state is TicketState.Valid or TicketState.NotYetValid ? Convert.ToBase64String(ticket.PublicRef) : null,
            ticket.CredentialVersion, device is not null && ticket.BoundDeviceId == device.Id, bound,
            Math.Max(0, Ticket.MaxRebinds - ticket.RebindCount),
            device is null ? null : Convert.ToBase64String(QrPayload.ShortDeviceId(device.Id)),
            device?.PublicKey is not null && HardwareLevels.Contains(device.AttestationStatus ?? ""),
            access is { EventId: not null } && state == TicketState.Valid ? access.Title : null);
    }

    /// <summary>Registreert de publieke hardwaresleutel van dit toestel (SubjectPublicKeyInfo, EC P-256).</summary>
    public async Task RegisterDeviceKeyAsync(Guid userId, string? installationId, string publicKey, string securityLevel, CancellationToken cancellationToken)
    {
        var device = await CurrentDeviceAsync(userId, installationId, cancellationToken);
        if (!SecurityLevels.Contains(securityLevel))
        {
            throw new DomainException(ErrorCodes.DeviceKeyInvalid, "Onbekend beveiligingsniveau van de sleutel.");
        }

        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(publicKey);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(spki, out _);
            if (key.KeySize != 256)
            {
                throw new CryptographicException();
            }
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            throw new DomainException(ErrorCodes.DeviceKeyInvalid, "De sleutel van dit toestel is ongeldig (verwacht: EC P-256).");
        }

        var replaced = device.PublicKey is not null && device.PublicKey != publicKey;
        device.PublicKey = Convert.ToBase64String(spki);
        device.AttestationStatus = securityLevel;
        if (replaced)
        {
            // Nieuwe sleutel: codes met de oude sleutel vervallen; het ticket moet opnieuw worden gekoppeld (telt niet mee).
            await db.Tickets.Where(t => t.BoundDeviceId == device.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.BoundDeviceId, (Guid?)null), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("device.key-registered", "Device", device.Id.ToString(), null,
            JsonSerializer.Serialize(new { securityLevel, replaced })), cancellationToken);
    }

    /// <summary>Challenge voor proof-of-possession: de app ondertekent die met de hardwaresleutel bij het koppelen.</summary>
    public async Task<string> ChallengeAsync(Guid userId, CancellationToken cancellationToken, Guid? childMemberId = null)
    {
        var (ticket, _, _) = await TicketAsync(userId, create: true, cancellationToken, childMemberId);
        if (ticket is null)
        {
            throw new DomainException(ErrorCodes.TicketUnavailable, "Je hebt geen ledenticket.", DomainErrorKind.Conflict);
        }

        var challenge = RandomNumberGenerator.GetBytes(32);
        ticket.BindChallengeHash = Convert.ToHexStringLower(SHA256.HashData(challenge));
        ticket.BindChallengeExpiresAt = Now + ChallengeLifetime;
        await db.SaveChangesAsync(cancellationToken);
        return Convert.ToBase64String(challenge);
    }

    /// <summary>
    /// Koppelt het ticket aan dit toestel. Met een hardwaresleutel alleen met een geldige handtekening over de challenge.
    /// Overzetten van een ander toestel kan <see cref="Ticket.MaxRebinds"/> keer per carnavalsjaar; daarna via het bestuur.
    /// </summary>
    public async Task BindAsync(
        Guid userId, string? installationId, string? challenge, string? signature, CancellationToken cancellationToken, Guid? childMemberId = null)
    {
        var device = await CurrentDeviceAsync(userId, installationId, cancellationToken);
        var (ticket, member, _) = await TicketAsync(userId, create: true, cancellationToken, childMemberId);
        if (ticket is null || member?.MembershipStatus != MembershipStatus.Active || ticket.Status == TicketStatus.Blocked)
        {
            throw new DomainException(ErrorCodes.TicketUnavailable, "Je hebt geen geldig ledenticket.", DomainErrorKind.Conflict);
        }

        if (device.PublicKey is not null)
        {
            var valid = challenge is not null && signature is not null
                && ticket.BindChallengeHash is not null && ticket.BindChallengeExpiresAt > Now
                && TryBase64(challenge) is { } bytes && Convert.ToHexStringLower(SHA256.HashData(bytes)) == ticket.BindChallengeHash
                && TryBase64(signature) is { } sig
                && TicketQrValidator.Verify(Convert.FromBase64String(device.PublicKey), bytes, sig);
            if (!valid)
            {
                throw new DomainException(ErrorCodes.DeviceKeyInvalid, "Dit toestel kon niet bewijzen dat het de sleutel heeft. Probeer het opnieuw.");
            }
        }

        if (ticket.BoundDeviceId == device.Id)
        {
            return;
        }

        var previous = ticket.BoundDeviceId;
        if (previous is not null)
        {
            if (ticket.RebindCount >= Ticket.MaxRebinds)
            {
                throw new DomainException(ErrorCodes.TicketRebindLimit,
                    $"Je hebt je ticket dit carnavalsjaar al {Ticket.MaxRebinds} keer naar een ander toestel overgezet. Het bestuur kan je helpen.",
                    DomainErrorKind.Conflict);
            }

            ticket.RebindCount++;
        }

        ticket.BoundDeviceId = device.Id;
        ticket.BoundAt = Now;
        ticket.BindChallengeHash = null;
        ticket.BindChallengeExpiresAt = null;
        ticket.UpdatedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("ticket.bound", "Ticket", ticket.Id.ToString(),
            JsonSerializer.Serialize(new { deviceId = previous }), JsonSerializer.Serialize(new { deviceId = device.Id, ticket.RebindCount })), cancellationToken);
    }

    /// <summary>Door de server ondertekende code voor toestellen zonder hardwaresleutel (alleen online, 45 seconden geldig).</summary>
    public async Task<ServerCode> ServerCodeAsync(
        Guid userId, string? installationId, CancellationToken cancellationToken, Guid? childMemberId = null, QrPurpose purpose = QrPurpose.Access)
    {
        var device = await CurrentDeviceAsync(userId, installationId, cancellationToken);
        var ticket = await GetAsync(userId, installationId, cancellationToken, childMemberId);
        // De munten-QR (fase 19b) werkt ook vóór carnaval, bijvoorbeeld op de pronkzitting.
        var usable = ticket.State == TicketState.Valid || (purpose == QrPurpose.Tokens && ticket.State == TicketState.NotYetValid);
        if (!usable || !ticket.BoundToThisDevice)
        {
            throw new DomainException(ErrorCodes.TicketUnavailable,
                usable ? "Je ticket staat op een ander toestel." : ticket.Message, DomainErrorKind.Conflict);
        }

        if (ticket.DeviceHasHardwareKey)
        {
            throw new DomainException(ErrorCodes.TicketUnavailable, "Dit toestel maakt de code zelf met zijn hardwaresleutel.", DomainErrorKind.Conflict);
        }

        var issuedAt = clock.UtcNow.ToUnixTimeSeconds();
        var unsigned = QrPayload.Unsigned(purpose == QrPurpose.Tokens ? QrPayload.ServerSignedTokens : QrPayload.ServerSigned, Convert.FromBase64String(ticket.PublicRef!), ticket.CredentialVersion,
            QrPayload.ShortDeviceId(device.Id), issuedAt, QrPayload.DefaultValidFor);
        using var key = await keys.ActivePrivateKeyAsync(cancellationToken);
        var signature = key.SignData(unsigned, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new ServerCode(Base45.Encode([.. unsigned, .. signature]), issuedAt, QrPayload.DefaultValidFor);
    }

    private static byte[]? TryBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
