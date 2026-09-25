using Drammers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Drammers.Infrastructure.Configuration;

/// <summary>Publieke app-configuratie (<c>GET /app-config</c>, docs/05 §2).</summary>
public sealed record AppConfigSnapshot(
    string MinAppVersionIos,
    string MinAppVersionAndroid,
    string RecommendedAppVersion,
    bool MaintenanceMode,
    string? MaintenanceMessage,
    string? SupportEmail,
    IReadOnlyDictionary<string, bool> Features);

/// <summary>
/// Leest de configuratie uit de database met een korte cache, zodat een wijziging zonder deploy binnen
/// <see cref="CacheDuration"/> zichtbaar is (fase 2: ≤ 60 s).
/// </summary>
public sealed class AppConfigReader(DrammersDbContext db, IMemoryCache cache)
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private const string CacheKey = "app-config";

    public async Task<AppConfigSnapshot> GetAsync(CancellationToken cancellationToken) =>
        (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await LoadAsync(cancellationToken);
        }))!;

    private async Task<AppConfigSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await db.AppConfiguration.AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        // Alleen vlaggen zonder doelgroep zijn publiek; doelgroepen volgen in fase 3.
        var features = await db.FeatureFlags.AsNoTracking()
            .Where(f => f.Audience == null)
            .ToDictionaryAsync(f => f.Key, f => f.Enabled, cancellationToken);

        string Get(string key, string fallback) =>
            settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        string? GetOptional(string key) =>
            settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        return new AppConfigSnapshot(
            Get(AppConfigurationKeys.MinAppVersionIos, "1.0.0"),
            Get(AppConfigurationKeys.MinAppVersionAndroid, "1.0.0"),
            Get(AppConfigurationKeys.RecommendedAppVersion, "1.0.0"),
            bool.TryParse(GetOptional(AppConfigurationKeys.MaintenanceMode), out var maintenance) && maintenance,
            GetOptional(AppConfigurationKeys.MaintenanceMessage),
            GetOptional(AppConfigurationKeys.SupportEmail),
            features);
    }
}
