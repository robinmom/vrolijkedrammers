using Drammers.SharedKernel.Persistence;

namespace Drammers.Infrastructure.Configuration;

/// <summary>Feature flag (<c>config.FeatureFlag</c>); zonder <see cref="Audience"/> geldt de vlag voor iedereen.</summary>
public sealed class FeatureFlag : IAuditable
{
    public required string Key { get; set; }

    public bool Enabled { get; set; }

    /// <summary>JSON met doelgroep (rollen/groepen); vanaf fase 3 geëvalueerd.</summary>
    public string? Audience { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Sleutel/waarde-configuratie (<c>config.AppConfiguration</c>), zonder deploy aan te passen.</summary>
public sealed class AppConfigurationSetting : IAuditable
{
    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

public enum RetentionAction
{
    Delete,
    Anonymize,
    Aggregate,
}

/// <summary>Bewaartermijn per gegevenssoort (<c>config.RetentionPolicy</c>, docs/06 §13).</summary>
public sealed class RetentionPolicy : IAuditable
{
    public required string DataType { get; set; }

    public int RetentionDays { get; set; }

    public RetentionAction Action { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Laatste uitvoering van een tijdgestuurde job over alle instanties heen (<c>config.ScheduledJob</c>).</summary>
public sealed class ScheduledJobState
{
    public required string Name { get; set; }

    public DateTime? LastStartedAt { get; set; }

    public DateTime? LastCompletedAt { get; set; }

    public DateTime? LastSucceededAt { get; set; }

    public string? LastError { get; set; }

    public string? LastInstance { get; set; }
}

/// <summary>Sleutels in <c>config.AppConfiguration</c> (docs/04 §12).</summary>
public static class AppConfigurationKeys
{
    public const string MinAppVersionIos = "min_app_version_ios";
    public const string MinAppVersionAndroid = "min_app_version_android";
    public const string RecommendedAppVersion = "recommended_app_version";
    public const string MaintenanceMode = "maintenance_mode";
    public const string MaintenanceMessage = "maintenance_message";
    public const string SupportEmail = "support_email";
}
