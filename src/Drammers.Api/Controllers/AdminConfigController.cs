using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Content;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Configuratie: app-versies en onderhoud, feature flags, bewaartermijnen (docs/05 §6).</summary>
[ApiController]
[Route("api/v1/admin/config")]
[RequirePermission(Permissions.ConfigManage)]
public sealed class AdminConfigController(DrammersDbContext db, AppConfigReader reader, ConfigurationAdministration administration, AppTextCatalog catalog) : ControllerBase
{
    [HttpGet("app-config")]
    [ProducesResponseType<AppConfigSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<AppConfigSettingsResponse> GetAppConfig(CancellationToken cancellationToken)
    {
        reader.Invalidate();
        var c = await reader.GetAsync(cancellationToken);
        return new AppConfigSettingsResponse(c.MinAppVersionIos, c.MinAppVersionAndroid, c.RecommendedAppVersion, c.MaintenanceMode, c.MaintenanceMessage, c.SupportEmail);
    }

    [HttpPut("app-config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateAppConfig(AppConfigSettingsRequest request, CancellationToken cancellationToken)
    {
        await administration.UpdateAppConfigAsync(new AppConfigUpdate(
            request.MinAppVersionIos, request.MinAppVersionAndroid, request.RecommendedAppVersion,
            request.MaintenanceMode, request.MaintenanceMessage, request.SupportEmail), cancellationToken);
        return NoContent();
    }

    [HttpGet("feature-flags")]
    [ProducesResponseType<IReadOnlyList<FeatureFlagResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<FeatureFlagResponse>> GetFeatureFlags(CancellationToken cancellationToken) =>
        await db.FeatureFlags.AsNoTracking().OrderBy(f => f.Key)
            .Select(f => new FeatureFlagResponse(f.Key, f.Enabled, f.Description, f.Audience != null))
            .ToListAsync(cancellationToken);

    [HttpPut("feature-flags/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetFeatureFlag([RegularExpression("^[a-z][a-z0-9.-]{1,98}$")] string key, FeatureFlagRequest request, CancellationToken cancellationToken)
    {
        await administration.SetFeatureFlagAsync(key, request.Enabled, request.Description, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Alle zichtbare teksten van de app, met de aanpassing als die er is. De lijst komt uit de catalogus die bij de
    /// app-build hoort, dus hij past bij de app die in de store staat zolang API en app samen zijn uitgerold.
    /// </summary>
    [HttpGet("app-texts")]
    [ProducesResponseType<IReadOnlyList<AppTextResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AppTextResponse>> GetAppTexts(CancellationToken cancellationToken)
    {
        var overrides = await db.AppTextOverrides.AsNoTracking().ToDictionaryAsync(t => t.Key, t => t.Value, cancellationToken);
        return catalog.Entries
            .Select(e => new AppTextResponse(e.Key, e.Text, overrides.GetValueOrDefault(e.Key),
                e.Places.Select(p => new AppTextPlaceResponse(p.File, p.Where)).ToList()))
            .OrderBy(t => t.Default, StringComparer.CurrentCulture)
            .ToList();
    }

    [HttpPut("app-texts/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAppText([RegularExpression("^[0-9a-f]{16}$")] string key, AppTextRequest request, CancellationToken cancellationToken)
    {
        var entry = catalog.Find(key);
        if (entry is null)
        {
            return NotFound();
        }

        await administration.SetAppTextAsync(key, entry.Text, request.Value, cancellationToken);
        return NoContent();
    }

    /// <summary>Zet de tekst terug op die uit de app-bundel.</summary>
    [HttpDelete("app-texts/{key}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetAppText([RegularExpression("^[0-9a-f]{16}$")] string key, CancellationToken cancellationToken)
    {
        await administration.ResetAppTextAsync(key, cancellationToken);
        return NoContent();
    }

    [HttpGet("retention")]
    [ProducesResponseType<IReadOnlyList<RetentionPolicyResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<RetentionPolicyResponse>> GetRetention(CancellationToken cancellationToken) =>
        await db.RetentionPolicies.AsNoTracking().OrderBy(r => r.DataType)
            .Select(r => new RetentionPolicyResponse(r.DataType, r.RetentionDays, r.Action.ToString()))
            .ToListAsync(cancellationToken);

    [HttpPut("retention/{dataType}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRetention(string dataType, RetentionPolicyRequest request, CancellationToken cancellationToken)
    {
        await administration.UpdateRetentionAsync(dataType, request.RetentionDays, request.Action, cancellationToken);
        return NoContent();
    }
}

public sealed record AppConfigSettingsResponse(
    string MinAppVersionIos, string MinAppVersionAndroid, string RecommendedAppVersion, bool MaintenanceMode, string? MaintenanceMessage, string? SupportEmail);

public sealed record AppConfigSettingsRequest(
    [Required, RegularExpression(@"^\d+\.\d+\.\d+$")] string MinAppVersionIos,
    [Required, RegularExpression(@"^\d+\.\d+\.\d+$")] string MinAppVersionAndroid,
    [Required, RegularExpression(@"^\d+\.\d+\.\d+$")] string RecommendedAppVersion,
    bool MaintenanceMode,
    [StringLength(500)] string? MaintenanceMessage,
    [EmailAddress, StringLength(254)] string? SupportEmail);

public sealed record FeatureFlagResponse(string Key, bool Enabled, string? Description, bool HasAudience);

public sealed record FeatureFlagRequest(bool Enabled, [StringLength(500)] string? Description);

public sealed record RetentionPolicyResponse(string DataType, int RetentionDays, string Action);

public sealed record RetentionPolicyRequest([Range(1, 3650)] int RetentionDays, RetentionAction Action);

/// <summary>Een tekst uit de app: de tekst uit de bundel, de aanpassing (als die er is) en waar hij staat.</summary>
public sealed record AppTextResponse(string Key, string Default, string? Value, IReadOnlyList<AppTextPlaceResponse> Places);

public sealed record AppTextPlaceResponse(string File, string Where);

public sealed record AppTextRequest([Required][StringLength(2000, MinimumLength = 1)] string Value);
