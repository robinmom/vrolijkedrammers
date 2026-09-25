using Drammers.Infrastructure.Configuration;
using Drammers.Modules.Audit.AuditLog;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Identity.Provisioning;
using Drammers.Modules.Identity.Roles;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Notification.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Persistence;

/// <summary>
/// De ene <c>DbContext</c> van de applicatie (OQ-62), met een eigen schema per module. Modules verwijzen niet naar
/// elkaars tabellen; de architectuurtests bewaken de grenzen in code.
/// </summary>
public sealed class DrammersDbContext(DbContextOptions<DrammersDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<LoginHistory> LoginHistory => Set<LoginHistory>();

    public DbSet<AccountProvisioning> AccountProvisioning => Set<AccountProvisioning>();

    public DbSet<CarnivalYear> CarnivalYears => Set<CarnivalYear>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    public DbSet<AppConfigurationSetting> AppConfiguration => Set<AppConfigurationSetting>();

    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    public DbSet<ScheduledJobState> ScheduledJobs => Set<ScheduledJobState>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelConventions.ConfigureConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DrammersDbContext).Assembly);
        ModelConventions.Apply(modelBuilder);
    }
}
