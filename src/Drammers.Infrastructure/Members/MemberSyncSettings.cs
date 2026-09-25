using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Import.Sync;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <summary>
/// Mapping van de vrije velden (B-06) in <c>config.AppConfiguration</c>. Een wijziging wordt geaudit; na een wijziging
/// hoort eerst een dry-run (ADR-010).
/// </summary>
public sealed class MemberSyncSettings(DrammersDbContext db, IAuditLogger audit)
{
    public const string MappingKey = "eboekhouden_member_mapping";

    /// <summary>Feature flag: de nachtelijke sync draait alleen als deze aan staat.</summary>
    public const string ScheduleFlag = "members-sync";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MemberFieldMapping> GetMappingAsync(CancellationToken cancellationToken)
    {
        var value = await db.AppConfiguration.AsNoTracking().Where(s => s.Key == MappingKey).Select(s => s.Value).SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? MemberFieldMapping.Default : JsonSerializer.Deserialize<MemberFieldMapping>(value, Json) ?? MemberFieldMapping.Default;
    }

    public async Task SetMappingAsync(MemberFieldMapping mapping, CancellationToken cancellationToken)
    {
        if (mapping.Validate() is { } error)
        {
            throw new DomainException(ErrorCodes.Validation, error, DomainErrorKind.Validation);
        }

        var normalized = mapping with { InactiveStatusValues = [.. mapping.InactiveStatusValues.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)] };
        var value = JsonSerializer.Serialize(normalized, Json);
        var setting = await db.AppConfiguration.SingleOrDefaultAsync(s => s.Key == MappingKey, cancellationToken);
        var before = setting?.Value;
        if (setting is null)
        {
            db.AppConfiguration.Add(new AppConfigurationSetting { Key = MappingKey, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("config.member-mapping.changed", "AppConfiguration", MappingKey, before, value), cancellationToken);
    }
}
