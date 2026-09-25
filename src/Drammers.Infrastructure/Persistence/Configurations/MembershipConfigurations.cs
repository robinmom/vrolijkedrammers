using Drammers.Modules.Import.Sync;
using Drammers.Modules.Membership.Members;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.ToTable("Member", Schemas.Membership);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.MemberNumber).HasMaxLength(15);
        builder.HasIndex(m => m.MemberNumber).IsUnique();
        builder.HasIndex(m => m.EbMemberId).IsUnique().HasFilter("[eb_member_id] IS NOT NULL");
        builder.Property(m => m.FullName).HasMaxLength(100);
        builder.Property(m => m.Salutation).HasMaxLength(50);
        builder.Property(m => m.Gender).HasMaxLength(1).IsUnicode(false).IsFixedLength();
        builder.Property(m => m.AddressLine).HasMaxLength(150);
        builder.Property(m => m.PostalCode).HasMaxLength(50);
        builder.Property(m => m.City).HasMaxLength(50);
        builder.Property(m => m.Country).HasMaxLength(50);
        builder.Property(m => m.Email).HasMaxLength(150);
        builder.HasIndex(m => m.Email);
        builder.Property(m => m.Phone).HasMaxLength(50);
        builder.Property(m => m.MobilePhone).HasMaxLength(50);
        builder.Property(m => m.EbStatusRaw).HasMaxLength(100);
        builder.Property(m => m.MemberCategory).HasMaxLength(50);
        builder.Property(m => m.FirstName).HasMaxLength(100);
        builder.Property(m => m.NamePrefix).HasMaxLength(30);
        builder.Property(m => m.LastName).HasMaxLength(100);
        builder.Property(m => m.EbHash).HasMaxLength(32).IsFixedLength();
        builder.Ignore(m => m.EffectiveStatus);
        builder.HasIndex(m => new { m.MembershipStatus, m.LastName });
    }
}

internal sealed class SyncJobConfiguration : IEntityTypeConfiguration<SyncJob>
{
    public void Configure(EntityTypeBuilder<SyncJob> builder)
    {
        builder.ToTable("SyncJob", Schemas.Import);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.ErrorMessage).HasMaxLength(500);
        builder.HasIndex(j => j.RequestedAt);
        builder.HasIndex(j => j.Status);
    }
}

internal sealed class SyncJobItemConfiguration : IEntityTypeConfiguration<SyncJobItem>
{
    public void Configure(EntityTypeBuilder<SyncJobItem> builder)
    {
        builder.ToTable("SyncJobItem", Schemas.Import);
        builder.Property(i => i.MemberNumber).HasMaxLength(20);
        builder.Property(i => i.ChangedFields).HasMaxLength(300).IsUnicode(false);
        builder.Property(i => i.Message).HasMaxLength(500);
        builder.HasOne<SyncJob>().WithMany().HasForeignKey(i => i.SyncJobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => new { i.SyncJobId, i.Action });
    }
}

internal sealed class SyncConflictConfiguration : IEntityTypeConfiguration<SyncConflict>
{
    public void Configure(EntityTypeBuilder<SyncConflict> builder)
    {
        builder.ToTable("SyncConflict", Schemas.Import);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.MemberNumber).HasMaxLength(20);
        builder.Property(c => c.Details).HasMaxLength(1000);
        builder.Property(c => c.ResolutionNote).HasMaxLength(500);
        builder.HasOne<SyncJob>().WithMany().HasForeignKey(c => c.SyncJobId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.Status);
    }
}
