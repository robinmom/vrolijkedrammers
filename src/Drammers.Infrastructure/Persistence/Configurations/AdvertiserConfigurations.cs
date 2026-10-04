using Drammers.Modules.Membership.Advertisers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class AdvertiserConfiguration : IEntityTypeConfiguration<Advertiser>
{
    public void Configure(EntityTypeBuilder<Advertiser> builder)
    {
        builder.ToTable("Advertiser", Schemas.Membership);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.HasIndex(a => a.Number).IsUnique();
        builder.Property(a => a.CompanyName).HasMaxLength(200);
        builder.Property(a => a.ContactName).HasMaxLength(150);
        builder.Property(a => a.Phone).HasMaxLength(30);
        builder.Property(a => a.Mobile).HasMaxLength(30);
        builder.Property(a => a.Email).HasMaxLength(254);
        builder.Property(a => a.AddressLine).HasMaxLength(200);
        builder.Property(a => a.PostalCode).HasMaxLength(10);
        builder.Property(a => a.City).HasMaxLength(100);
        builder.Property(a => a.Website).HasMaxLength(200);
        builder.Property(a => a.Page).HasMaxLength(50);
        builder.Property(a => a.IbanProtected).HasMaxLength(1000);
        builder.Property(a => a.IbanLast4).HasMaxLength(4);
        builder.Property(a => a.MandateReference).HasMaxLength(35);
        builder.Property(a => a.ImportedCollectorName).HasMaxLength(150);
        builder.Property(a => a.Notes).HasMaxLength(2000);
        builder.HasOne<Drammers.Modules.Membership.Members.Member>().WithMany().HasForeignKey(a => a.CollectorMemberId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(a => a.CollectorMemberId);
        builder.HasMany(a => a.Years).WithOne().HasForeignKey(y => y.AdvertiserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AdvertiserYearConfiguration : IEntityTypeConfiguration<AdvertiserYear>
{
    public void Configure(EntityTypeBuilder<AdvertiserYear> builder)
    {
        builder.ToTable("AdvertiserYear", Schemas.Membership);
        builder.HasKey(y => new { y.AdvertiserId, y.Year });
        builder.Property(y => y.Amount).HasPrecision(9, 2);
        builder.Property(y => y.Note).HasMaxLength(500);
        builder.HasIndex(y => new { y.Year, y.Status });
    }
}

internal sealed class AdvertiserInvoiceConfiguration : IEntityTypeConfiguration<AdvertiserInvoice>
{
    public void Configure(EntityTypeBuilder<AdvertiserInvoice> builder)
    {
        builder.ToTable("AdvertiserInvoice", Schemas.Membership);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Number).HasMaxLength(20);
        builder.HasIndex(i => i.Number).IsUnique();
        builder.HasIndex(i => new { i.AdvertiserId, i.Year }).IsUnique();
        builder.HasIndex(i => new { i.Year, i.Sequence }).IsUnique();
        builder.Property(i => i.Amount).HasPrecision(9, 2);
        builder.Property(i => i.Description).HasMaxLength(140);
        builder.Property(i => i.CompanyName).HasMaxLength(200);
        builder.Property(i => i.ContactName).HasMaxLength(150);
        builder.Property(i => i.AddressLine).HasMaxLength(200);
        builder.Property(i => i.PostalCode).HasMaxLength(10);
        builder.Property(i => i.City).HasMaxLength(100);
        builder.Property(i => i.Email).HasMaxLength(254);
        builder.Property(i => i.MandateReference).HasMaxLength(35);
        builder.Property(i => i.IbanLast4).HasMaxLength(4);
        builder.HasOne<Advertiser>().WithMany().HasForeignKey(i => i.AdvertiserId).OnDelete(DeleteBehavior.Restrict);
    }
}
