using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.News;
using Drammers.Modules.Content.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class EventCategoryConfiguration : IEntityTypeConfiguration<EventCategory>
{
    public void Configure(EntityTypeBuilder<EventCategory> builder)
    {
        builder.ToTable("EventCategory", Schemas.Content);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(40).IsUnicode(false);
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.HasData(
            new EventCategory { Id = 1, Code = "carnaval", Name = "Carnaval", SortOrder = 10 },
            new EventCategory { Id = 2, Code = "jeugd", Name = "Jeugd", SortOrder = 20 },
            new EventCategory { Id = 3, Code = "vereniging", Name = "Vereniging", SortOrder = 30 },
            new EventCategory { Id = 4, Code = "kader", Name = "Kader", SortOrder = 40 });
    }
}

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Event", Schemas.Content);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Summary).HasMaxLength(500);
        builder.Property(e => e.LocationName).HasMaxLength(200);
        builder.Property(e => e.LocationAddress).HasMaxLength(300);
        builder.Property(e => e.Latitude).HasPrecision(9, 6);
        builder.Property(e => e.Longitude).HasPrecision(9, 6);
        builder.Property(e => e.ImageBlobPath).HasMaxLength(300);
        builder.Property(e => e.BadgeText).HasMaxLength(40);
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.HasOne<Modules.Content.CarnivalYears.CarnivalYear>().WithMany().HasForeignKey(e => e.CarnivalYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Audiences).WithOne().HasForeignKey(a => a.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Attachments).WithOne().HasForeignKey(a => a.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.Status, e.StartAt });
    }
}

internal sealed class EventAudienceConfiguration : IEntityTypeConfiguration<EventAudience>
{
    public void Configure(EntityTypeBuilder<EventAudience> builder)
    {
        builder.ToTable("EventAudience", Schemas.Content);
        builder.HasKey(a => new { a.EventId, a.AudienceType, a.AudienceRef });
        builder.Property(a => a.AudienceRef).HasMaxLength(64).IsUnicode(false);
    }
}

internal sealed class EventAttachmentConfiguration : IEntityTypeConfiguration<EventAttachment>
{
    public void Configure(EntityTypeBuilder<EventAttachment> builder)
    {
        builder.ToTable("EventAttachment", Schemas.Content);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.FileName).HasMaxLength(200);
        builder.Property(a => a.BlobPath).HasMaxLength(300);
        builder.Property(a => a.ContentType).HasMaxLength(100).IsUnicode(false);
    }
}

internal sealed class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    public void Configure(EntityTypeBuilder<NewsItem> builder)
    {
        builder.ToTable("News", Schemas.Content);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Title).HasMaxLength(200);
        builder.Property(n => n.Summary).HasMaxLength(500);
        builder.Property(n => n.Category).HasMaxLength(50);
        builder.Property(n => n.ImageBlobPath).HasMaxLength(300);
        builder.Property(n => n.RowVersion).IsRowVersion();
        builder.HasMany(n => n.Audiences).WithOne().HasForeignKey(a => a.NewsId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.Status, n.PublishAt });
    }
}

internal sealed class NewsAudienceConfiguration : IEntityTypeConfiguration<NewsAudience>
{
    public void Configure(EntityTypeBuilder<NewsAudience> builder)
    {
        builder.ToTable("NewsAudience", Schemas.Content);
        builder.HasKey(a => new { a.NewsId, a.AudienceType, a.AudienceRef });
        builder.Property(a => a.AudienceRef).HasMaxLength(64).IsUnicode(false);
    }
}

internal sealed class PhotoAlbumConfiguration : IEntityTypeConfiguration<PhotoAlbum>
{
    public void Configure(EntityTypeBuilder<PhotoAlbum> builder)
    {
        builder.ToTable("PhotoAlbum", Schemas.Content);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Title).HasMaxLength(200);
        builder.Property(a => a.Description).HasMaxLength(2000);
        builder.HasMany(a => a.Audiences).WithOne().HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.Status, a.AlbumDate });
    }
}

internal sealed class PhotoAlbumAudienceConfiguration : IEntityTypeConfiguration<PhotoAlbumAudience>
{
    public void Configure(EntityTypeBuilder<PhotoAlbumAudience> builder)
    {
        builder.ToTable("PhotoAlbumAudience", Schemas.Content);
        builder.HasKey(a => new { a.AlbumId, a.AudienceType, a.AudienceRef });
        builder.Property(a => a.AudienceRef).HasMaxLength(64).IsUnicode(false);
    }
}

internal sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("Photo", Schemas.Content);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.OriginalBlobPath).HasMaxLength(300);
        builder.Property(p => p.DisplayBlobPath).HasMaxLength(300);
        builder.Property(p => p.ThumbnailBlobPath).HasMaxLength(300);
        builder.Property(p => p.Caption).HasMaxLength(500);
        builder.Property(p => p.Photographer).HasMaxLength(100);
        builder.HasOne<PhotoAlbum>().WithMany().HasForeignKey(p => p.AlbumId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.AlbumId, p.SortOrder });
    }
}
