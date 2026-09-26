using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Privacy;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Identity;

public sealed record DeviceRegistration(string InstallationId, DevicePlatform Platform, string? Model, string? AppVersion);

public sealed record PrivacyExportLink(Guid Id, DateTime ExpiresAt, Uri? DownloadUrl);

/// <summary>
/// Het eigen account van een lid (fase 9): apparaten, account verwijderen en de AVG-export. Beheerders gebruiken
/// dezelfde apparaatacties via het portal (intrekken met <c>member.block</c>).
/// </summary>
public sealed class MyAccount(
    DrammersDbContext db,
    IAuditLogger audit,
    IEntraUserDirectory entra,
    IFileStore files,
    ICurrentActor actor,
    IClock clock,
    AccountAdministration administration)
{
    /// <summary>Een export is 24 uur te downloaden (fase 9); de link zelf is steeds maar 15 minuten geldig (ADR-008).</summary>
    public static readonly TimeSpan ExportLifetime = TimeSpan.FromHours(24);

    /// <summary>Hoe vaak het tijdstip "laatst gezien" hooguit wordt bijgewerkt.</summary>
    public static readonly TimeSpan LastSeenResolution = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // ----- Apparaten ----------------------------------------------------------------------------------------------

    /// <summary>Meldt de installatie aan na het inloggen (idempotent). Een afgemeld apparaat moet een nieuwe id maken.</summary>
    public async Task<Device> RegisterDeviceAsync(Guid userId, DeviceRegistration registration, CancellationToken cancellationToken)
    {
        var installationId = registration.InstallationId.Trim();
        if (installationId.Length is < 16 or > 64)
        {
            throw new DomainException(ErrorCodes.Validation, "Ongeldige installatie-id.");
        }

        var now = clock.UtcNow.UtcDateTime;
        var device = await db.Devices.SingleOrDefaultAsync(d => d.UserId == userId && d.InstallationId == installationId, cancellationToken);
        if (device is { Status: DeviceStatus.Revoked })
        {
            throw new DomainException(ErrorCodes.DeviceRevoked, "Dit apparaat is afgemeld. Log opnieuw in.", DomainErrorKind.Conflict);
        }

        var model = Clip(registration.Model, 100);
        if (device is null)
        {
            device = new Device
            {
                Id = IdGenerator.NewId(),
                UserId = userId,
                InstallationId = installationId,
                Name = model ?? (registration.Platform == DevicePlatform.Ios ? "iPhone" : "Android-telefoon"),
                CreatedAt = now,
                Status = DeviceStatus.Active,
            };
            db.Devices.Add(device);
        }

        device.Platform = registration.Platform;
        device.Model = model;
        device.AppVersion = Clip(registration.AppVersion, 20);
        device.LastSeenAt = now;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("device.registered", "Device", device.Id.ToString(), null, JsonSerializer.Serialize(new { platform = device.Platform.ToString(), device.Model }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return device;
    }

    public async Task RenameDeviceAsync(Guid userId, Guid deviceId, string name, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 100)
        {
            throw new DomainException(ErrorCodes.Validation, "Geef het apparaat een naam van 1 tot 100 tekens.");
        }

        var device = await FindDeviceAsync(deviceId, userId, cancellationToken);
        device.Name = trimmed;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Meldt een apparaat af: de API weigert daarna elke aanroep met die installatie-id (401 <c>DEVICE_REVOKED</c>),
    /// waarop de app de tokens wist. <paramref name="userId"/> is <c>null</c> voor een beheerder.
    /// </summary>
    public async Task RevokeDeviceAsync(Guid? userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await FindDeviceAsync(deviceId, userId, cancellationToken);
        if (device.Status == DeviceStatus.Revoked)
        {
            return;
        }

        device.Status = DeviceStatus.Revoked;
        device.RevokedAt = clock.UtcNow.UtcDateTime;
        device.RevokedBy = actor.UserId;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("device.revoked", "Device", device.Id.ToString(), null, JsonSerializer.Serialize(new { byAdministrator = userId is null }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Status van een installatie voor de apparaatcheck bij elke aanroep; werkt "laatst gezien" bij.</summary>
    public async Task<DeviceStatus?> TouchDeviceAsync(Guid userId, string installationId, CancellationToken cancellationToken)
    {
        var device = await db.Devices.SingleOrDefaultAsync(d => d.UserId == userId && d.InstallationId == installationId, cancellationToken);
        if (device is null)
        {
            return null;
        }

        var now = clock.UtcNow.UtcDateTime;
        if (device.Status == DeviceStatus.Active && now - device.LastSeenAt > LastSeenResolution)
        {
            device.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return device.Status;
    }

    private async Task<Device> FindDeviceAsync(Guid deviceId, Guid? userId, CancellationToken cancellationToken) =>
        await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId && (userId == null || d.UserId == userId), cancellationToken)
        ?? throw new DomainException(ErrorCodes.DeviceNotFound, "Apparaat niet gevonden.", DomainErrorKind.NotFound);

    // ----- Account verwijderen ------------------------------------------------------------------------------------

    /// <summary>
    /// Verwijdert het eigen account: rollen, apparaten en koppeling met het lid vervallen, de naam en het e-mailadres
    /// worden vervangen, en het Entra-account wordt verwijderd. De ledenadministratie (e-Boekhouden) blijft ongemoeid;
    /// de auditregels blijven (bewaartermijn).
    /// </summary>
    public async Task DeleteAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var objectId = await administration.DeleteUserAsync(userId, cancellationToken);
        await entra.DeleteAsync(objectId, cancellationToken);
    }

    // ----- AVG-export ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Maakt een export van alle gegevens over de gebruiker (account, lid, groepen, rollen, apparaten, aanmeldingen,
    /// verzoeken) als JSON in de privé-container <c>exports</c>. Geaudit.
    /// </summary>
    public async Task<PrivacyExportLink> CreateExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        var request = new PrivacyRequest
        {
            Id = IdGenerator.NewId(),
            UserId = userId,
            MemberId = user.MemberId,
            Type = PrivacyRequestType.Export,
            Status = PrivacyRequestStatus.Requested,
            RequestedAt = now,
        };
        db.PrivacyRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        var content = await BuildExportAsync(user, now, cancellationToken);
        var path = $"privacy/{request.Id}.json";
        await using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content)))
        {
            await files.UploadAsync(FileContainers.Exports, path, stream, "application/json", cancellationToken);
        }

        request.Status = PrivacyRequestStatus.Completed;
        request.CompletedAt = now;
        request.FilePath = path;
        request.ExpiresAt = now + ExportLifetime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("privacy.exported", "User", userId.ToString(), null, JsonSerializer.Serialize(new { requestId = request.Id }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PrivacyExportLink(request.Id, request.ExpiresAt.Value, await files.GetReadUriAsync(FileContainers.Exports, path, cancellationToken));
    }

    /// <summary>Een verse downloadlink voor een eigen export zolang die niet verlopen is.</summary>
    public async Task<PrivacyExportLink> GetExportAsync(Guid userId, Guid requestId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var request = await db.PrivacyRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == requestId && r.UserId == userId && r.Type == PrivacyRequestType.Export, cancellationToken);
        if (request is not { FilePath: { } path, ExpiresAt: { } expiresAt } || expiresAt <= now)
        {
            throw new DomainException(ErrorCodes.PrivacyExportNotFound, "Deze export bestaat niet (meer). Vraag een nieuwe aan.", DomainErrorKind.NotFound);
        }

        return new PrivacyExportLink(request.Id, expiresAt, await files.GetReadUriAsync(FileContainers.Exports, path, cancellationToken));
    }

    private async Task<string> BuildExportAsync(User user, DateTime now, CancellationToken cancellationToken)
    {
        var member = user.MemberId is { } memberId
            ? await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            : null;
        var groups = member is null
            ? []
            : await db.GroupMemberships.AsNoTracking().Where(gm => gm.MemberId == member.Id)
                .Join(db.Groups, gm => gm.GroupId, g => g.Id, (gm, g) => new { group = g.Name, function = gm.Function.ToString(), gm.ValidFrom, gm.ValidTo })
                .ToListAsync(cancellationToken);
        var roles = await db.UserRoles.AsNoTracking().Where(ur => ur.UserId == user.Id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { role = r.Name, ur.ValidFrom, ur.ValidTo, ur.AssignedAt })
            .ToListAsync(cancellationToken);
        var devices = await db.Devices.AsNoTracking().Where(d => d.UserId == user.Id)
            .Select(d => new { d.Name, platform = d.Platform.ToString(), d.Model, d.AppVersion, status = d.Status.ToString(), d.CreatedAt, d.LastSeenAt, d.RevokedAt })
            .ToListAsync(cancellationToken);
        var logins = await db.LoginHistory.AsNoTracking().Where(l => l.UserId == user.Id).OrderByDescending(l => l.OccurredAt)
            .Select(l => new { l.OccurredAt, result = l.Result.ToString(), l.Reason, l.UserAgent })
            .ToListAsync(cancellationToken);
        var accountRequests = member is null
            ? []
            : await db.AccountRequests.AsNoTracking().Where(r => r.MemberId == member.Id)
                .Select(r => new { r.MemberNumber, r.Email, status = r.Status.ToString(), r.RequestedAt, r.DecidedAt })
                .ToListAsync(cancellationToken);
        var privacyRequests = await db.PrivacyRequests.AsNoTracking().Where(r => r.UserId == user.Id)
            .Select(r => new { type = r.Type.ToString(), status = r.Status.ToString(), r.RequestedAt, r.CompletedAt })
            .ToListAsync(cancellationToken);

        var export = new
        {
            exportedAt = now,
            description = "Alle gegevens die de app van De Vrolijke Drammers over jou bewaart (AVG art. 15). De ledenadministratie zelf staat in e-Boekhouden; vraag het secretariaat om een uittreksel daarvan.",
            account = new { user.Email, user.DisplayName, status = user.AccountStatus.ToString(), user.CreatedAt, user.LastLoginAt },
            member = member is null ? null : new
            {
                member.MemberNumber,
                member.FullName,
                member.FirstName,
                member.NamePrefix,
                member.LastName,
                member.Salutation,
                member.AddressLine,
                member.PostalCode,
                member.City,
                member.Country,
                member.Email,
                member.Phone,
                member.MobilePhone,
                member.BirthDate,
                member.JoinYear,
                member.MemberCategory,
                status = member.EffectiveStatus.ToString(),
                member.MembershipValidFrom,
                member.MembershipValidTo,
                member.EbLastSeenAt,
            },
            groups,
            roles,
            devices,
            logins,
            accountRequests,
            privacyRequests,
        };
        return JsonSerializer.Serialize(export, Json);
    }

    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
