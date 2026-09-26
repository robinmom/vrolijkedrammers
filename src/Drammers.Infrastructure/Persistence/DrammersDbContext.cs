using Drammers.Infrastructure.Configuration;
using Drammers.Modules.Audit.AuditLog;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.News;
using Drammers.Modules.Content.Photos;
using Drammers.Modules.Identity.AccountRequests;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Identity.Provisioning;
using Drammers.Modules.Identity.Roles;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Import.Sync;
using Drammers.Modules.Membership.Groups;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Membership.Privacy;
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

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<AccountRequest> AccountRequests => Set<AccountRequest>();

    public DbSet<PrivacyRequest> PrivacyRequests => Set<PrivacyRequest>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();

    public DbSet<SyncJobItem> SyncJobItems => Set<SyncJobItem>();

    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();

    public DbSet<CarnivalYear> CarnivalYears => Set<CarnivalYear>();

    public DbSet<EventCategory> EventCategories => Set<EventCategory>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<EventAttachment> EventAttachments => Set<EventAttachment>();

    public DbSet<NewsItem> News => Set<NewsItem>();

    public DbSet<PhotoAlbum> PhotoAlbums => Set<PhotoAlbum>();

    public DbSet<Photo> Photos => Set<Photo>();

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
