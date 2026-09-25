using Drammers.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal static class Seed
{
    /// <summary>Vaste tijd voor seed-data, zodat migraties deterministisch blijven.</summary>
    public static readonly DateTime CreatedAt = new(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
}

internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> builder)
    {
        builder.ToTable("FeatureFlag", Schemas.Config);
        builder.HasKey(f => f.Key);
        builder.Property(f => f.Key).HasMaxLength(100).IsUnicode(false);
        builder.Property(f => f.Description).HasMaxLength(500);
    }
}

internal sealed class AppConfigurationSettingConfiguration : IEntityTypeConfiguration<AppConfigurationSetting>
{
    public void Configure(EntityTypeBuilder<AppConfigurationSetting> builder)
    {
        builder.ToTable("AppConfiguration", Schemas.Config);
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100).IsUnicode(false);
        builder.Property(s => s.Value).HasMaxLength(1000);

        builder.HasData(
            Setting(AppConfigurationKeys.MinAppVersionIos, "1.0.0"),
            Setting(AppConfigurationKeys.MinAppVersionAndroid, "1.0.0"),
            Setting(AppConfigurationKeys.RecommendedAppVersion, "1.0.0"),
            Setting(AppConfigurationKeys.MaintenanceMode, "false"),
            Setting(AppConfigurationKeys.MaintenanceMessage, string.Empty),
            Setting(AppConfigurationKeys.SupportEmail, string.Empty));
    }

    private static AppConfigurationSetting Setting(string key, string value) =>
        new() { Key = key, Value = value, CreatedAt = Seed.CreatedAt };
}

internal sealed class RetentionPolicyConfiguration : IEntityTypeConfiguration<RetentionPolicy>
{
    public void Configure(EntityTypeBuilder<RetentionPolicy> builder)
    {
        builder.ToTable("RetentionPolicy", Schemas.Config);
        builder.HasKey(r => r.DataType);
        builder.Property(r => r.DataType).HasMaxLength(60).IsUnicode(false);

        // Voorstel uit docs/06 §13 ("Bewaartermijnen"); aan te passen via het beheerportal.
        builder.HasData(
            Policy("member_former", 730, RetentionAction.Anonymize),
            Policy("membership_application_rejected", 183, RetentionAction.Delete),
            Policy("guardian_account_without_child", 183, RetentionAction.Delete),
            Policy("account_request_rejected", 92, RetentionAction.Delete),
            Policy("login_history", 365, RetentionAction.Delete),
            Policy("ticket_scan", 730, RetentionAction.Aggregate),
            Policy("ticket_order_payment", 2557, RetentionAction.Delete),
            Policy("parade_registration", 1096, RetentionAction.Anonymize),
            Policy("parade_document", 365, RetentionAction.Delete),
            Policy("notification", 365, RetentionAction.Delete),
            Policy("push_token_inactive", 183, RetentionAction.Delete),
            Policy("audit_log", 730, RetentionAction.Delete),
            Policy("audit_log_financial_privacy", 2557, RetentionAction.Delete));
    }

    private static RetentionPolicy Policy(string dataType, int days, RetentionAction action) =>
        new() { DataType = dataType, RetentionDays = days, Action = action, CreatedAt = Seed.CreatedAt };
}

internal sealed class ScheduledJobStateConfiguration : IEntityTypeConfiguration<ScheduledJobState>
{
    public void Configure(EntityTypeBuilder<ScheduledJobState> builder)
    {
        builder.ToTable("ScheduledJob", Schemas.Config);
        builder.HasKey(j => j.Name);
        builder.Property(j => j.Name).HasMaxLength(100).IsUnicode(false);
        builder.Property(j => j.LastError).HasMaxLength(2000);
        builder.Property(j => j.LastInstance).HasMaxLength(100);
    }
}
