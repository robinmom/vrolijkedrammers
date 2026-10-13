using Drammers.Modules.Content.Festival;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class DanceFestivalConfiguration : IEntityTypeConfiguration<DanceFestival>
{
    public void Configure(EntityTypeBuilder<DanceFestival> builder)
    {
        builder.ToTable("DanceFestival", Schemas.Content, t =>
        {
            t.HasCheckConstraint("CK_DanceFestival_capacity", "[capacity] >= 0");
            t.HasCheckConstraint("CK_DanceFestival_prices", "[price_solo_duo_cents] >= 0 AND [price_group_one_dance_cents] >= 0 AND [price_group_two_dances_cents] >= 0");
        });
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Name).HasMaxLength(100);
        builder.Property(f => f.Location).HasMaxLength(200);
        builder.Property(f => f.NotificationEmail).HasMaxLength(254);
        builder.Property(f => f.RulesBlobPath).HasMaxLength(300);
        builder.Property(f => f.PrivacyBlobPath).HasMaxLength(300);
        // Precies één actieve editie.
        builder.HasIndex(f => f.Active).IsUnique().HasFilter("[active] = 1");
    }
}

internal sealed class DanceFestivalRegistrationConfiguration : IEntityTypeConfiguration<DanceFestivalRegistration>
{
    public void Configure(EntityTypeBuilder<DanceFestivalRegistration> builder)
    {
        builder.ToTable("DanceFestivalRegistration", Schemas.Content);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ClubName).HasMaxLength(150);
        builder.Property(r => r.ContactName).HasMaxLength(100);
        builder.Property(r => r.AddressLine).HasMaxLength(200);
        builder.Property(r => r.PostalCode).HasMaxLength(10);
        builder.Property(r => r.City).HasMaxLength(100);
        builder.Property(r => r.Phone).HasMaxLength(30);
        builder.Property(r => r.Email).HasMaxLength(254);
        builder.Property(r => r.Remarks).HasMaxLength(2000);
        builder.Property(r => r.InfoRequest).HasMaxLength(2000);
        builder.Property(r => r.AccessTokenProtected).HasMaxLength(500).IsUnicode(false);
        builder.HasIndex(r => new { r.FestivalId, r.Number }).IsUnique();
        builder.HasOne<DanceFestival>().WithMany().HasForeignKey(r => r.FestivalId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Entries).WithOne().HasForeignKey(e => e.RegistrationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DanceFestivalEntryConfiguration : IEntityTypeConfiguration<DanceFestivalEntry>
{
    public void Configure(EntityTypeBuilder<DanceFestivalEntry> builder)
    {
        builder.ToTable("DanceFestivalEntry", Schemas.Content, t =>
        {
            t.HasCheckConstraint("CK_DanceFestivalEntry_dancers", "[dancer_count] >= 1 AND [dancer_count] <= 200");
            t.HasCheckConstraint("CK_DanceFestivalEntry_age", "[average_age] >= 0 AND [average_age] <= 99");
        });
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Names).HasMaxLength(300);
        builder.Property(e => e.LeaderName).HasMaxLength(100);
        builder.Property(e => e.Title).HasMaxLength(150);
        builder.Property(e => e.AverageAge).HasPrecision(4, 1);
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.HasIndex(e => new { e.RegistrationId, e.SortOrder });
    }
}

internal sealed class DanceFestivalRegistrationVersionConfiguration : IEntityTypeConfiguration<DanceFestivalRegistrationVersion>
{
    public void Configure(EntityTypeBuilder<DanceFestivalRegistrationVersion> builder)
    {
        builder.ToTable("DanceFestivalRegistrationVersion", Schemas.Content);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.HasIndex(v => new { v.RegistrationId, v.Version }).IsUnique();
        builder.HasOne<DanceFestivalRegistration>().WithMany().HasForeignKey(v => v.RegistrationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DanceFestivalPaymentConfiguration : IEntityTypeConfiguration<DanceFestivalPayment>
{
    public void Configure(EntityTypeBuilder<DanceFestivalPayment> builder)
    {
        builder.ToTable("DanceFestivalPayment", Schemas.Payments, t => t.HasCheckConstraint("CK_DanceFestivalPayment_amount", "[amount_cents] > 0"));
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.MolliePaymentId).HasMaxLength(40).IsUnicode(false);
        builder.Property(p => p.MollieStatus).HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(p => p.MolliePaymentId).HasFilter("[mollie_payment_id] IS NOT NULL");
        builder.HasIndex(p => p.RegistrationId);
        builder.HasOne<DanceFestivalRegistration>().WithMany().HasForeignKey(p => p.RegistrationId).OnDelete(DeleteBehavior.Cascade);
    }
}
