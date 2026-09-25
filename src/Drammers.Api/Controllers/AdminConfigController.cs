using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
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
public sealed class AdminConfigController(DrammersDbContext db, AppConfigReader reader, ConfigurationAdministration administration) : ControllerBase
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
