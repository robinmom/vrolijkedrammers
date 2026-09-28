using Drammers.Modules.Notification.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("Outbox", Schemas.Notification);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(100).IsUnicode(false);
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // Alleen onverwerkte berichten worden gepolld.
        builder.HasIndex(m => m.CreatedAt).HasFilter("[processed_at] IS NULL");
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Modules.Notification.Notifications.Notification>
{
    public void Configure(EntityTypeBuilder<Modules.Notification.Notifications.Notification> builder)
    {
        builder.ToTable("Notification", Schemas.Notification);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Title).HasMaxLength(Modules.Notification.Notifications.Notification.TitleMaxLength);
        builder.Property(n => n.Body).HasMaxLength(Modules.Notification.Notifications.Notification.BodyMaxLength);
        builder.Property(n => n.DeepLink).HasMaxLength(200);
        builder.Property(n => n.SourceType).HasMaxLength(30).IsUnicode(false);
        builder.HasIndex(n => n.CreatedAt);
        // Eén push per bron (nieuwsbericht): voorkomt een tweede melding bij opnieuw publiceren.
        builder.HasIndex(n => new { n.SourceType, n.SourceId }).IsUnique().HasFilter("[source_id] IS NOT NULL");
    }
}

internal sealed class NotificationRecipientConfiguration : IEntityTypeConfiguration<Modules.Notification.Notifications.NotificationRecipient>
{
    public void Configure(EntityTypeBuilder<Modules.Notification.Notifications.NotificationRecipient> builder)
    {
        builder.ToTable("NotificationRecipient", Schemas.Notification);
        builder.HasOne<Modules.Notification.Notifications.Notification>().WithMany().HasForeignKey(r => r.NotificationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.NotificationId, r.UserId }).IsUnique().HasFilter("[user_id] IS NOT NULL");
        builder.HasIndex(r => new { r.UserId, r.NotificationId }).HasFilter("[user_id] IS NOT NULL");
    }
}

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<Modules.Notification.Notifications.NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<Modules.Notification.Notifications.NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDelivery", Schemas.Notification);
        builder.HasOne<Modules.Notification.Notifications.NotificationRecipient>().WithMany().HasForeignKey(d => d.RecipientId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(d => d.TicketId).HasMaxLength(100).IsUnicode(false);
        builder.Property(d => d.ErrorCode).HasMaxLength(100).IsUnicode(false);
        builder.HasIndex(d => new { d.NotificationId, d.Status });
        builder.HasIndex(d => d.PushDeviceId);
    }
}

internal sealed class PushDeviceConfiguration : IEntityTypeConfiguration<Modules.Notification.Notifications.PushDevice>
{
    public void Configure(EntityTypeBuilder<Modules.Notification.Notifications.PushDevice> builder)
    {
        builder.ToTable("PushDevice", Schemas.Notification);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.AnonymousInstallId).HasMaxLength(64).IsUnicode(false);
        builder.Property(p => p.Platform).HasMaxLength(10).IsUnicode(false);
        builder.Property(p => p.ProtectedToken).HasMaxLength(1000).IsUnicode(false);
        builder.Property(p => p.TokenHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.HasIndex(p => p.TokenHash).IsUnique();
        builder.HasIndex(p => p.DeviceId).IsUnique().HasFilter("[device_id] IS NOT NULL");
        builder.HasIndex(p => p.AnonymousInstallId).IsUnique().HasFilter("[anonymous_install_id] IS NOT NULL");
        builder.HasIndex(p => p.UserId);
        builder.HasOne<Modules.Identity.Devices.Device>().WithMany().HasForeignKey(p => p.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Modules.Identity.Users.User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<Modules.Notification.Notifications.NotificationPreference>
{
    public void Configure(EntityTypeBuilder<Modules.Notification.Notifications.NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreference", Schemas.Notification);
        builder.HasKey(p => new { p.UserId, p.Category });
        builder.HasOne<Modules.Identity.Users.User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
