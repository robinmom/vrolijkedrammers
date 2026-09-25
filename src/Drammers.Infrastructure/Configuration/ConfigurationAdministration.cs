using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Configuration;

public sealed record AppConfigUpdate(
    string MinAppVersionIos, string MinAppVersionAndroid, string RecommendedAppVersion,
    bool MaintenanceMode, string? MaintenanceMessage, string? SupportEmail);

public sealed record CarnivalYearInput(string Name, DateOnly StartDate, DateOnly EndDate, DateOnly CarnivalStartDate, DateOnly CarnivalEndDate);

/// <summary>
/// Beheer van configuratie en carnavalsjaren (fase 4, <c>config.manage</c>). Elke wijziging wordt geaudit; de publieke
/// app-configuratie is na een wijziging direct zichtbaar op deze instantie.
/// </summary>
public sealed class ConfigurationAdministration(DrammersDbContext db, IAuditLogger audit, AppConfigReader appConfig)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task UpdateAppConfigAsync(AppConfigUpdate update, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            [AppConfigurationKeys.MinAppVersionIos] = update.MinAppVersionIos,
            [AppConfigurationKeys.MinAppVersionAndroid] = update.MinAppVersionAndroid,
            [AppConfigurationKeys.RecommendedAppVersion] = update.RecommendedAppVersion,
            [AppConfigurationKeys.MaintenanceMode] = update.MaintenanceMode ? "true" : "false",
            [AppConfigurationKeys.MaintenanceMessage] = update.MaintenanceMessage ?? string.Empty,
            [AppConfigurationKeys.SupportEmail] = update.SupportEmail ?? string.Empty,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var settings = await db.AppConfiguration.ToDictionaryAsync(s => s.Key, cancellationToken);
        var before = settings.ToDictionary(s => s.Key, s => s.Value.Value);
        foreach (var (key, value) in values)
        {
            if (settings.TryGetValue(key, out var setting))
            {
                setting.Value = value;
            }
            else
            {
                db.AppConfiguration.Add(new AppConfigurationSetting { Key = key, Value = value });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("config.app-config.changed", "AppConfiguration", "app-config",
            JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(values, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        appConfig.Invalidate();
    }

    public async Task SetFeatureFlagAsync(string key, bool enabled, string? description, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var flag = await db.FeatureFlags.SingleOrDefaultAsync(f => f.Key == key, cancellationToken);
        var before = flag is null ? null : JsonSerializer.Serialize(new { flag.Enabled, flag.Description }, Json);
        if (flag is null)
        {
            db.FeatureFlags.Add(new FeatureFlag { Key = key, Enabled = enabled, Description = description });
        }
        else
        {
            flag.Enabled = enabled;
            flag.Description = description;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("config.feature-flag.changed", "FeatureFlag", key, before,
            JsonSerializer.Serialize(new { enabled, description }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        appConfig.Invalidate();
    }

    public async Task UpdateRetentionAsync(string dataType, int retentionDays, RetentionAction action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var policy = await db.RetentionPolicies.SingleOrDefaultAsync(r => r.DataType == dataType, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ConfigKeyUnknown, $"Onbekende gegevenssoort '{dataType}'.", DomainErrorKind.NotFound);
        var before = JsonSerializer.Serialize(new { policy.RetentionDays, policy.Action }, Json);
        policy.RetentionDays = retentionDays;
        policy.Action = action;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("config.retention.changed", "RetentionPolicy", dataType, before,
            JsonSerializer.Serialize(new { retentionDays, action }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CarnivalYear> CreateCarnivalYearAsync(CarnivalYearInput input, CancellationToken cancellationToken)
    {
        Validate(input);
        if (await db.CarnivalYears.AnyAsync(y => y.Name == input.Name, cancellationToken))
        {
            throw new DomainException(ErrorCodes.CarnivalYearNameTaken, $"Carnavalsjaar '{input.Name}' bestaat al.", DomainErrorKind.Conflict);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var year = new CarnivalYear
        {
            Name = input.Name,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            CarnivalStartDate = input.CarnivalStartDate,
            CarnivalEndDate = input.CarnivalEndDate,
            Active = !await db.CarnivalYears.AnyAsync(y => y.Active, cancellationToken),
        };
        db.CarnivalYears.Add(year);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-year.created", "CarnivalYear", year.Id.ToString(), null, JsonSerializer.Serialize(input, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return year;
    }

    public async Task UpdateCarnivalYearAsync(int id, CarnivalYearInput input, CancellationToken cancellationToken)
    {
        Validate(input);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var year = await FindYearAsync(id, cancellationToken);
        if (await db.CarnivalYears.AnyAsync(y => y.Name == input.Name && y.Id != id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.CarnivalYearNameTaken, $"Carnavalsjaar '{input.Name}' bestaat al.", DomainErrorKind.Conflict);
        }

        var before = JsonSerializer.Serialize(new CarnivalYearInput(year.Name, year.StartDate, year.EndDate, year.CarnivalStartDate, year.CarnivalEndDate), Json);
        (year.Name, year.StartDate, year.EndDate, year.CarnivalStartDate, year.CarnivalEndDate) =
            (input.Name, input.StartDate, input.EndDate, input.CarnivalStartDate, input.CarnivalEndDate);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-year.updated", "CarnivalYear", id.ToString(), before, JsonSerializer.Serialize(input, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Maakt dit jaar actief en de andere inactief, in één transactie: er is altijd precies één actief jaar.</summary>
    public async Task ActivateCarnivalYearAsync(int id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var year = await FindYearAsync(id, cancellationToken);
        var previous = await db.CarnivalYears.Where(y => y.Active && y.Id != id).Select(y => y.Id).ToListAsync(cancellationToken);
        await db.CarnivalYears.Where(y => y.Active && y.Id != id).ExecuteUpdateAsync(s => s.SetProperty(y => y.Active, false), cancellationToken);
        year.Active = true;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-year.activated", "CarnivalYear", id.ToString(),
            JsonSerializer.Serialize(new { previouslyActive = previous }, Json), null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<CarnivalYear> FindYearAsync(int id, CancellationToken cancellationToken) =>
        await db.CarnivalYears.SingleOrDefaultAsync(y => y.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.CarnivalYearNotFound, "Carnavalsjaar niet gevonden.", DomainErrorKind.NotFound);

    private static void Validate(CarnivalYearInput input)
    {
        if (input.StartDate >= input.EndDate
            || input.CarnivalStartDate > input.CarnivalEndDate
            || input.CarnivalStartDate < input.StartDate
            || input.CarnivalEndDate > input.EndDate)
        {
            throw new DomainException(
                ErrorCodes.CarnivalYearInvalidDates,
                "Controleer de datums: het seizoen moet vóór het einde beginnen en de carnavalsdagen moeten binnen het seizoen vallen.");
        }
    }
}
