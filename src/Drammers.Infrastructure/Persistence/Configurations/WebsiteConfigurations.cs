using Drammers.Modules.Content.Website;
using Drammers.Modules.Membership.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class WebsiteSettingsConfiguration : IEntityTypeConfiguration<WebsiteSettings>
{
    public void Configure(EntityTypeBuilder<WebsiteSettings> builder)
    {
        builder.ToTable("WebsiteSettings", Schemas.Content, t => t.HasCheckConstraint("CK_WebsiteSettings_singleton", "[id] = 1"));
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.HeroEyebrow).HasMaxLength(80);
        builder.Property(s => s.HeroTitle).HasMaxLength(120);
        builder.Property(s => s.HeroSubtitle).HasMaxLength(300);
        builder.Property(s => s.HeroPrimaryLabel).HasMaxLength(40);
        builder.Property(s => s.HeroSecondaryLabel).HasMaxLength(40);
        builder.Property(s => s.HeroImageBlobPath).HasMaxLength(300);
        builder.Property(s => s.FacebookPageUrl).HasMaxLength(300);
        builder.Property(s => s.InstagramUrl).HasMaxLength(300);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasData(new WebsiteSettings
        {
            Id = WebsiteSettings.SingletonId,
            HeroEyebrow = "CARNAVAL · LOIL",
            HeroTitle = "Alaaf! Het feest komt eraan.",
            HeroSubtitle = "Pronkzitting, optocht, dansgarde en vier dagen feest. Volg alles van De Vrolijke Drammers hier en in onze app.",
            HeroPrimaryLabel = "Bekijk de agenda",
            HeroPrimaryLink = WebsiteLink.Agenda,
            HeroSecondaryLabel = "Word lid",
            HeroSecondaryLink = WebsiteLink.Membership,
            FacebookPageUrl = "https://www.facebook.com/vrolijkedrammers",
            InstagramUrl = "https://www.instagram.com/vrolijkedrammers",
            CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}

internal sealed class WebsitePageConfiguration : IEntityTypeConfiguration<WebsitePage>
{
    public void Configure(EntityTypeBuilder<WebsitePage> builder)
    {
        builder.ToTable("WebsitePage", Schemas.Content);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Slug).HasMaxLength(120).IsUnicode(false);
        builder.HasIndex(p => p.Slug).IsUnique();
        builder.Property(p => p.Title).HasMaxLength(200);
        builder.Property(p => p.Intro).HasMaxLength(500);
        builder.Property(p => p.ImageBlobPath).HasMaxLength(300);
    }
}

internal sealed class CommitteeConfiguration : IEntityTypeConfiguration<Committee>
{
    public void Configure(EntityTypeBuilder<Committee> builder)
    {
        builder.ToTable("Committee", Schemas.Content);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.Slug).HasMaxLength(120).IsUnicode(false);
        builder.HasIndex(c => c.Slug).IsUnique();
        builder.HasData(
            new Committee { Id = 1, Name = "Bestuur", Slug = "bestuur", SortOrder = 10 },
            new Committee { Id = 2, Name = "Raad van Elf", Slug = "raad-van-elf", SortOrder = 20 },
            new Committee { Id = 3, Name = "Convent", Slug = "convent", SortOrder = 30 },
            new Committee { Id = 4, Name = "Leiding dansgarde", Slug = "leiding-dansgarde", SortOrder = 40 });
    }
}

internal sealed class CommitteeMemberConfiguration : IEntityTypeConfiguration<CommitteeMember>
{
    public void Configure(EntityTypeBuilder<CommitteeMember> builder)
    {
        builder.ToTable("CommitteeMember", Schemas.Content);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Name).HasMaxLength(150);
        builder.Property(m => m.Function).HasMaxLength(100);
        builder.Property(m => m.PhotoBlobPath).HasMaxLength(300);
        builder.HasOne<Committee>().WithMany().HasForeignKey(m => m.CommitteeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Member>().WithMany().HasForeignKey(m => m.MemberId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(m => new { m.CommitteeId, m.SortOrder });
    }
}

internal sealed class PrinceConfiguration : IEntityTypeConfiguration<Prince>
{
    public void Configure(EntityTypeBuilder<Prince> builder)
    {
        builder.ToTable("Prince", Schemas.Content);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.PrinceName).HasMaxLength(100);
        builder.Property(p => p.Name).HasMaxLength(150);
        builder.Property(p => p.Motto).HasMaxLength(500);
        builder.Property(p => p.PhotoBlobPath).HasMaxLength(300);
        builder.HasIndex(p => new { p.Kind, p.Year });
    }
}

internal sealed class AwardConfiguration : IEntityTypeConfiguration<Award>
{
    public void Configure(EntityTypeBuilder<Award> builder)
    {
        builder.ToTable("Award", Schemas.Content);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Recipient).HasMaxLength(150);
        builder.Property(a => a.PhotoBlobPath).HasMaxLength(300);
        builder.Property(a => a.Slug).HasMaxLength(120).IsUnicode(false);
        builder.HasIndex(a => a.Slug).IsUnique();
        builder.HasIndex(a => new { a.Year, a.Type });
    }
}

internal sealed class WebsiteImportItemConfiguration : IEntityTypeConfiguration<WebsiteImportItem>
{
    public void Configure(EntityTypeBuilder<WebsiteImportItem> builder)
    {
        builder.ToTable("WebsiteImportItem", Schemas.Content);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();
        builder.Property(i => i.SourceKey).HasMaxLength(300);
        builder.Property(i => i.SourceUrl).HasMaxLength(500);
        builder.Property(i => i.Title).HasMaxLength(300);
        builder.Property(i => i.TargetId).HasMaxLength(100);
        builder.Property(i => i.Error).HasMaxLength(2000);
        builder.HasIndex(i => new { i.Kind, i.SourceKey }).IsUnique();
        builder.HasIndex(i => new { i.Status, i.Id });
    }
}

internal sealed class WebsiteRedirectConfiguration : IEntityTypeConfiguration<WebsiteRedirect>
{
    public void Configure(EntityTypeBuilder<WebsiteRedirect> builder)
    {
        builder.ToTable("WebsiteRedirect", Schemas.Content);
        builder.HasKey(r => r.FromPath);
        builder.Property(r => r.FromPath).HasMaxLength(300).IsUnicode(false);
        builder.Property(r => r.ToPath).HasMaxLength(300).IsUnicode(false);
    }
}
