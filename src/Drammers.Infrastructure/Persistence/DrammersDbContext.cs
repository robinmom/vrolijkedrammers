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

    public DbSet<Drammers.Modules.Membership.Applications.MembershipApplication> MembershipApplications =>
        Set<Drammers.Modules.Membership.Applications.MembershipApplication>();

    public DbSet<Drammers.Modules.Membership.Guardians.GuardianRelation> GuardianRelations =>
        Set<Drammers.Modules.Membership.Guardians.GuardianRelation>();

    public DbSet<Drammers.Modules.Membership.Guardians.GuardianLinkRequest> GuardianLinkRequests =>
        Set<Drammers.Modules.Membership.Guardians.GuardianLinkRequest>();

    public DbSet<Drammers.Modules.Membership.Guardians.GuardianSuggestionDismissal> GuardianSuggestionDismissals =>
        Set<Drammers.Modules.Membership.Guardians.GuardianSuggestionDismissal>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<ExcludedMember> ExcludedMembers => Set<ExcludedMember>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();

    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();

    public DbSet<SyncJobItem> SyncJobItems => Set<SyncJobItem>();

    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();

    public DbSet<CarnivalYear> CarnivalYears => Set<CarnivalYear>();

    public DbSet<Modules.Ticketing.Tickets.Ticket> Tickets => Set<Modules.Ticketing.Tickets.Ticket>();

    public DbSet<Modules.Ticketing.Tickets.TicketSigningKey> TicketSigningKeys => Set<Modules.Ticketing.Tickets.TicketSigningKey>();

    public DbSet<Modules.Ticketing.Tickets.AccessScan> AccessScans => Set<Modules.Ticketing.Tickets.AccessScan>();

    public DbSet<Modules.Ticketing.Sales.SaleProduct> SaleProducts => Set<Modules.Ticketing.Sales.SaleProduct>();

    public DbSet<Modules.Ticketing.Sales.SaleOrder> SaleOrders => Set<Modules.Ticketing.Sales.SaleOrder>();

    public DbSet<Modules.Ticketing.Sales.SaleOrderSequence> SaleOrderSequences => Set<Modules.Ticketing.Sales.SaleOrderSequence>();

    public DbSet<Modules.Ticketing.Sales.OrderTicket> OrderTickets => Set<Modules.Ticketing.Sales.OrderTicket>();

    public DbSet<Modules.Ticketing.Sales.WaitlistEntry> WaitlistEntries => Set<Modules.Ticketing.Sales.WaitlistEntry>();

    public DbSet<Modules.Ticketing.Sales.TokenScan> TokenScans => Set<Modules.Ticketing.Sales.TokenScan>();

    public DbSet<EventCategory> EventCategories => Set<EventCategory>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<EventAttachment> EventAttachments => Set<EventAttachment>();

    public DbSet<NewsItem> News => Set<NewsItem>();

    public DbSet<PhotoAlbum> PhotoAlbums => Set<PhotoAlbum>();

    public DbSet<Photo> Photos => Set<Photo>();

    public DbSet<Modules.Content.Website.WebsiteSettings> WebsiteSettings => Set<Modules.Content.Website.WebsiteSettings>();

    public DbSet<Modules.Content.Website.WebsitePage> WebsitePages => Set<Modules.Content.Website.WebsitePage>();

    public DbSet<Modules.Content.Website.Committee> Committees => Set<Modules.Content.Website.Committee>();

    public DbSet<Modules.Content.Website.CommitteeMember> CommitteeMembers => Set<Modules.Content.Website.CommitteeMember>();

    public DbSet<Modules.Content.Website.Prince> Princes => Set<Modules.Content.Website.Prince>();

    public DbSet<Modules.Content.Website.Award> Awards => Set<Modules.Content.Website.Award>();

    public DbSet<Modules.Content.Website.WebsiteImportItem> WebsiteImportItems => Set<Modules.Content.Website.WebsiteImportItem>();

    public DbSet<Modules.Content.Website.WebsiteRedirect> WebsiteRedirects => Set<Modules.Content.Website.WebsiteRedirect>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    public DbSet<Modules.Parade.Parades.Parade> Parades => Set<Modules.Parade.Parades.Parade>();

    public DbSet<Modules.Parade.Parades.ParadeNumberSequence> ParadeNumberSequences => Set<Modules.Parade.Parades.ParadeNumberSequence>();

    public DbSet<Modules.Parade.Categories.ParadeCategory> ParadeCategories => Set<Modules.Parade.Categories.ParadeCategory>();

    public DbSet<Modules.Parade.Judging.ParadeJudgingCategory> ParadeJudgingCategories => Set<Modules.Parade.Judging.ParadeJudgingCategory>();

    public DbSet<Modules.Parade.Judging.ParadeJurorAssignment> ParadeJurorAssignments => Set<Modules.Parade.Judging.ParadeJurorAssignment>();

    public DbSet<Modules.Parade.Registrations.ParadeRegistration> ParadeRegistrations => Set<Modules.Parade.Registrations.ParadeRegistration>();

    public DbSet<Modules.Parade.Registrations.ParadeRegistrationManager> ParadeRegistrationManagers => Set<Modules.Parade.Registrations.ParadeRegistrationManager>();

    public DbSet<Modules.Parade.Registrations.ParadeStatusHistory> ParadeStatusHistory => Set<Modules.Parade.Registrations.ParadeStatusHistory>();

    public DbSet<Modules.Parade.Registrations.ParadeRegistrationHistory> ParadeRegistrationHistory => Set<Modules.Parade.Registrations.ParadeRegistrationHistory>();

    public DbSet<Modules.Parade.Registrations.ParadeDocument> ParadeDocuments => Set<Modules.Parade.Registrations.ParadeDocument>();

    public DbSet<Modules.Parade.Registrations.ParadeBuildLocation> ParadeBuildLocations => Set<Modules.Parade.Registrations.ParadeBuildLocation>();

    public DbSet<Modules.Parade.Registrations.ParadeStatusEditPolicy> ParadeStatusEditPolicies => Set<Modules.Parade.Registrations.ParadeStatusEditPolicy>();

    public DbSet<Modules.Notification.Notifications.Notification> Notifications => Set<Modules.Notification.Notifications.Notification>();

    public DbSet<Modules.Notification.Notifications.NotificationRecipient> NotificationRecipients => Set<Modules.Notification.Notifications.NotificationRecipient>();

    public DbSet<Modules.Notification.Notifications.NotificationDelivery> NotificationDeliveries => Set<Modules.Notification.Notifications.NotificationDelivery>();

    public DbSet<Modules.Notification.Notifications.PushDevice> PushDevices => Set<Modules.Notification.Notifications.PushDevice>();

    public DbSet<Modules.Notification.Notifications.NotificationPreference> NotificationPreferences => Set<Modules.Notification.Notifications.NotificationPreference>();

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
