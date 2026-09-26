using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>De ingelogde gebruiker: profiel, rollen, permissions en features (docs/05 §3); fase 9: lid, apparaten, AVG.</summary>
[ApiController]
[Route("api/v1/me")]
[RequireActiveUser]
public sealed class MeController(AppConfigReader appConfig, DrammersDbContext db, MyAccount account) : ControllerBase
{
    /// <summary>Permissions zijn alleen bedoeld voor het tonen of verbergen van UI; de server blijft leidend (docs/07 §1).</summary>
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<MeResponse> Get(CancellationToken cancellationToken)
    {
        var user = CurrentUser.Get(HttpContext)!;
        var config = await appConfig.GetAsync(cancellationToken);
        return new MeResponse(
            user.UserId,
            user.Email,
            user.DisplayName,
            user.MemberId,
            [.. user.Roles.Select(r => new MeRole(r.Code, r.Name))],
            [.. user.Permissions.Order(StringComparer.Ordinal)],
            config.Features);
    }

    /// <summary>Eigen lidgegevens (read-only; wijzigen via het secretariaat in e-Boekhouden) en groepen.</summary>
    [HttpGet("member")]
    [RequirePermission(Permissions.MemberReadOwn)]
    [ProducesResponseType<MyMemberResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<MyMemberResponse> GetMember(CancellationToken cancellationToken)
    {
        var memberId = CurrentUser.Get(HttpContext)!.MemberId
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Je account is niet aan een lid gekoppeld.", DomainErrorKind.NotFound);
        var m = await db.Members.AsNoTracking().SingleAsync(x => x.Id == memberId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var groups = await db.GroupMemberships.AsNoTracking()
            .Where(gm => gm.MemberId == memberId && (gm.ValidFrom == null || gm.ValidFrom <= today) && (gm.ValidTo == null || gm.ValidTo >= today))
            .Join(db.Groups.Where(g => g.Active), gm => gm.GroupId, g => g.Id, (gm, g) => new { g.Name, gm.Function })
            .OrderBy(g => g.Name)
            .Select(g => new MyGroupResponse(g.Name, g.Function))
            .ToListAsync(cancellationToken);
        return new MyMemberResponse(
            m.MemberNumber, m.FullName, m.FirstName, m.AddressLine, m.PostalCode, m.City, m.Email, m.Phone ?? m.MobilePhone,
            m.BirthDate, m.JoinYear, m.LocalStatusOverride ?? m.MembershipStatus, m.MembershipValidTo, groups);
    }

    [HttpGet("devices")]
    [ProducesResponseType<IReadOnlyList<DeviceResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<DeviceResponse>> GetDevices(CancellationToken cancellationToken)
    {
        var userId = CurrentUser.Get(HttpContext)!.UserId;
        var current = Request.Headers[DeviceCheck.HeaderName].ToString();
        var devices = await db.Devices.AsNoTracking().Where(d => d.UserId == userId && d.Status == DeviceStatus.Active)
            .OrderByDescending(d => d.LastSeenAt).ToListAsync(cancellationToken);
        return [.. devices.Select(d => DeviceResponse.From(d, d.InstallationId == current))];
    }

    /// <summary>Meldt deze installatie aan na het inloggen (idempotent).</summary>
    [HttpPost("devices")]
    [ProducesResponseType<DeviceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<DeviceResponse> RegisterDevice(RegisterDeviceRequest request, CancellationToken cancellationToken)
    {
        var device = await account.RegisterDeviceAsync(CurrentUser.Get(HttpContext)!.UserId,
            new DeviceRegistration(request.InstallationId, request.Platform, request.Model, request.AppVersion), cancellationToken);
        return DeviceResponse.From(device, current: true);
    }

    [HttpPatch("devices/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RenameDevice(Guid id, RenameDeviceRequest request, CancellationToken cancellationToken)
    {
        await account.RenameDeviceAsync(CurrentUser.Get(HttpContext)!.UserId, id, request.Name, cancellationToken);
        return NoContent();
    }

    /// <summary>Apparaat afmelden; dat apparaat is daarna uitgelogd (401 <c>DEVICE_REVOKED</c>).</summary>
    [HttpDelete("devices/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeDevice(Guid id, CancellationToken cancellationToken)
    {
        await account.RevokeDeviceAsync(CurrentUser.Get(HttpContext)!.UserId, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Account verwijderen: ook het Entra-account. De ledenadministratie in e-Boekhouden blijft ongemoeid.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAccount(DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        if (request.Confirmation != DeleteAccountRequest.Expected)
        {
            throw new DomainException(ErrorCodes.Validation, $"Typ '{DeleteAccountRequest.Expected}' om je account te verwijderen.");
        }

        await account.DeleteAccountAsync(CurrentUser.Get(HttpContext)!.UserId, cancellationToken);
        return NoContent();
    }

    /// <summary>AVG-export (inzage): JSON met alle gegevens, 24 uur te downloaden.</summary>
    [HttpPost("privacy/export")]
    [ProducesResponseType<PrivacyExportResponse>(StatusCodes.Status200OK)]
    public async Task<PrivacyExportResponse> RequestExport(CancellationToken cancellationToken) =>
        PrivacyExportResponse.From(await account.CreateExportAsync(CurrentUser.Get(HttpContext)!.UserId, cancellationToken));

    [HttpGet("privacy/export/{id:guid}")]
    [ProducesResponseType<PrivacyExportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PrivacyExportResponse> GetExport(Guid id, CancellationToken cancellationToken) =>
        PrivacyExportResponse.From(await account.GetExportAsync(CurrentUser.Get(HttpContext)!.UserId, id, cancellationToken));
}

public sealed record MeResponse(
    Guid Id,
    string Email,
    string DisplayName,
    Guid? MemberId,
    IReadOnlyList<MeRole> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyDictionary<string, bool> Features);

public sealed record MeRole(string Code, string Name);

public sealed record MyMemberResponse(
    string MemberNumber, string FullName, string? FirstName, string? AddressLine, string? PostalCode, string? City, string? Email,
    string? Phone, DateOnly? BirthDate, short? JoinYear, MembershipStatus Status, DateOnly? MembershipValidTo,
    IReadOnlyList<MyGroupResponse> Groups);

public sealed record MyGroupResponse(string Name, Modules.Membership.Groups.GroupFunction Function);

public sealed record DeviceResponse(
    Guid Id, string Name, DevicePlatform Platform, string? Model, string? AppVersion, DeviceStatus Status, DateTime CreatedAt,
    DateTime LastSeenAt, bool Current)
{
    public static DeviceResponse From(Device d, bool current) =>
        new(d.Id, d.Name, d.Platform, d.Model, d.AppVersion, d.Status, d.CreatedAt, d.LastSeenAt, current);
}

public sealed record RegisterDeviceRequest(
    [param: Required, StringLength(64, MinimumLength = 16)] string InstallationId,
    DevicePlatform Platform,
    [param: StringLength(100)] string? Model,
    [param: StringLength(20)] string? AppVersion);

public sealed record RenameDeviceRequest([param: Required, StringLength(100, MinimumLength = 1)] string Name);

public sealed record DeleteAccountRequest([param: Required] string Confirmation)
{
    public const string Expected = "VERWIJDEREN";
}

public sealed record PrivacyExportResponse(Guid Id, DateTime ExpiresAt, Uri? DownloadUrl)
{
    public static PrivacyExportResponse From(PrivacyExportLink link) => new(link.Id, link.ExpiresAt, link.DownloadUrl);
}
