using Drammers.Modules.Notification.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

// Fase 27a: mailings. Leden en groepen staan in een andere module; daarom alleen hun id, zonder foreign key.

internal sealed class MailingListConfiguration : IEntityTypeConfiguration<MailingList>
{
    public void Configure(EntityTypeBuilder<MailingList> builder)
    {
        builder.ToTable("MailingList", Schemas.Notification);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Name).HasMaxLength(100);
        builder.Property(l => l.Description).HasMaxLength(500);
        builder.HasMany(l => l.Members).WithOne().HasForeignKey(m => m.ListId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(l => l.Groups).WithOne().HasForeignKey(g => g.ListId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(l => l.Addresses).WithOne().HasForeignKey(a => a.ListId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MailingListMemberConfiguration : IEntityTypeConfiguration<MailingListMember>
{
    public void Configure(EntityTypeBuilder<MailingListMember> builder)
    {
        builder.ToTable("MailingListMember", Schemas.Notification);
        builder.HasKey(m => new { m.ListId, m.MemberId });
        builder.HasIndex(m => m.MemberId);
    }
}

internal sealed class MailingListGroupConfiguration : IEntityTypeConfiguration<MailingListGroup>
{
    public void Configure(EntityTypeBuilder<MailingListGroup> builder)
    {
        builder.ToTable("MailingListGroup", Schemas.Notification);
        builder.HasKey(g => new { g.ListId, g.GroupId });
    }
}

internal sealed class MailingListAddressConfiguration : IEntityTypeConfiguration<MailingListAddress>
{
    public void Configure(EntityTypeBuilder<MailingListAddress> builder)
    {
        builder.ToTable("MailingListAddress", Schemas.Notification);
        builder.HasKey(a => new { a.ListId, a.Email });
        builder.Property(a => a.Email).HasMaxLength(254);
        builder.Property(a => a.Name).HasMaxLength(150);
    }
}

internal sealed class MailingConfiguration : IEntityTypeConfiguration<Mailing>
{
    public void Configure(EntityTypeBuilder<Mailing> builder)
    {
        builder.ToTable("Mailing", Schemas.Notification);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Subject).HasMaxLength(200);
        builder.Property(m => m.Preheader).HasMaxLength(200);
        builder.HasMany(m => m.Lists).WithOne().HasForeignKey(t => t.MailingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.CreatedAt);
    }
}

internal sealed class MailingTargetConfiguration : IEntityTypeConfiguration<MailingTarget>
{
    public void Configure(EntityTypeBuilder<MailingTarget> builder)
    {
        builder.ToTable("MailingTarget", Schemas.Notification);
        builder.HasKey(t => new { t.MailingId, t.ListId });
        // Een groep met verstuurde mailings kan niet weg (de mailing verwijst ernaar).
        builder.HasOne<MailingList>().WithMany().HasForeignKey(t => t.ListId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MailingRecipientConfiguration : IEntityTypeConfiguration<MailingRecipient>
{
    public void Configure(EntityTypeBuilder<MailingRecipient> builder)
    {
        builder.ToTable("MailingRecipient", Schemas.Notification);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.Email).HasMaxLength(254);
        builder.Property(r => r.Name).HasMaxLength(150);
        builder.Property(r => r.FirstName).HasMaxLength(100);
        builder.Property(r => r.Error).HasMaxLength(1000);
        builder.HasOne<Mailing>().WithMany().HasForeignKey(r => r.MailingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.MailingId, r.Email }).IsUnique();
        builder.HasIndex(r => new { r.MailingId, r.Status });
    }
}

internal sealed class MailingUnsubscribeConfiguration : IEntityTypeConfiguration<MailingUnsubscribe>
{
    public void Configure(EntityTypeBuilder<MailingUnsubscribe> builder)
    {
        builder.ToTable("MailingUnsubscribe", Schemas.Notification);
        builder.HasKey(u => u.Email);
        builder.Property(u => u.Email).HasMaxLength(254);
    }
}
