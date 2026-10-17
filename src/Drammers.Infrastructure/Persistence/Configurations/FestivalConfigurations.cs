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
        builder.Property(f => f.AwardsCompletedBy).HasMaxLength(20);
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

internal sealed class DanceFestivalMolliePaymentConfiguration : IEntityTypeConfiguration<DanceFestivalMolliePayment>
{
    public void Configure(EntityTypeBuilder<DanceFestivalMolliePayment> builder)
    {
        builder.ToTable("DanceFestivalMolliePayment", Schemas.Payments);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.MolliePaymentId).HasMaxLength(40).IsUnicode(false);
        builder.Property(p => p.MollieStatus).HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(p => p.MolliePaymentId).IsUnique();
        builder.HasIndex(p => p.PaymentId);
        builder.HasOne<DanceFestivalPayment>().WithMany().HasForeignKey(p => p.PaymentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DanceFestivalProgramItemConfiguration : IEntityTypeConfiguration<DanceFestivalProgramItem>
{
    public void Configure(EntityTypeBuilder<DanceFestivalProgramItem> builder)
    {
        builder.ToTable("DanceFestivalProgramItem", Schemas.Content, t => t.HasCheckConstraint("CK_DanceFestivalProgramItem_minutes", "[minutes] >= 0 AND [minutes] <= 240"));
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.CategoryKey).HasMaxLength(60);
        builder.Property(i => i.Title).HasMaxLength(80);
        builder.HasIndex(i => new { i.FestivalId, i.SortOrder });
        builder.HasIndex(i => new { i.FestivalId, i.CategoryKey }).IsUnique().HasFilter("[category_key] IS NOT NULL");
        builder.HasOne<DanceFestival>().WithMany().HasForeignKey(i => i.FestivalId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FestivalJurorConfiguration : IEntityTypeConfiguration<FestivalJuror>
{
    public void Configure(EntityTypeBuilder<FestivalJuror> builder)
    {
        builder.ToTable("FestivalJuror", Schemas.Content);
        builder.HasKey(j => new { j.FestivalId, j.UserId });
        builder.HasIndex(j => new { j.FestivalId, j.Number }).IsUnique();
        builder.HasOne<DanceFestival>().WithMany().HasForeignKey(j => j.FestivalId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FestivalScoreConfiguration : IEntityTypeConfiguration<FestivalScore>
{
    public void Configure(EntityTypeBuilder<FestivalScore> builder)
    {
        builder.ToTable("FestivalScore", Schemas.Content, t => t.HasCheckConstraint("CK_FestivalScore_value", "[value] >= 0 AND [value] <= 10"));
        builder.HasKey(s => new { s.EntryId, s.UserId, s.Criterion });
        builder.HasIndex(s => new { s.FestivalId, s.UserId });
        builder.HasOne<DanceFestivalEntry>().WithMany().HasForeignKey(s => s.EntryId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FestivalCommentConfiguration : IEntityTypeConfiguration<FestivalComment>
{
    public void Configure(EntityTypeBuilder<FestivalComment> builder)
    {
        builder.ToTable("FestivalComment", Schemas.Content);
        builder.HasKey(c => new { c.EntryId, c.UserId });
        builder.Property(c => c.Text).HasMaxLength(1000);
        builder.HasIndex(c => new { c.FestivalId, c.UserId });
        builder.HasOne<DanceFestivalEntry>().WithMany().HasForeignKey(c => c.EntryId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FestivalCategorySubmissionConfiguration : IEntityTypeConfiguration<FestivalCategorySubmission>
{
    public void Configure(EntityTypeBuilder<FestivalCategorySubmission> builder)
    {
        builder.ToTable("FestivalCategorySubmission", Schemas.Content);
        builder.HasKey(s => new { s.FestivalId, s.CategoryKey, s.UserId });
        builder.Property(s => s.CategoryKey).HasMaxLength(60);
        builder.HasOne<DanceFestival>().WithMany().HasForeignKey(s => s.FestivalId).OnDelete(DeleteBehavior.Cascade);
    }
}
