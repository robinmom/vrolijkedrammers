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

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Drammers.Modules.Membership.Groups.Group>
{
    public void Configure(EntityTypeBuilder<Drammers.Modules.Membership.Groups.Group> builder)
    {
        builder.ToTable("Group", Schemas.Membership);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(100);
        builder.HasIndex(g => g.Name).IsUnique();
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.HasOne<Modules.Content.CarnivalYears.CarnivalYear>().WithMany().HasForeignKey(g => g.CarnivalYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(g => g.Memberships).WithOne().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GroupMembershipConfiguration : IEntityTypeConfiguration<Drammers.Modules.Membership.Groups.GroupMembership>
{
    public void Configure(EntityTypeBuilder<Drammers.Modules.Membership.Groups.GroupMembership> builder)
    {
        builder.ToTable("GroupMembership", Schemas.Membership);
        builder.HasKey(m => new { m.GroupId, m.MemberId });
        builder.HasOne<Member>().WithMany().HasForeignKey(m => m.MemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.MemberId);
    }
}

internal sealed class PrivacyRequestConfiguration : IEntityTypeConfiguration<Drammers.Modules.Membership.Privacy.PrivacyRequest>
{
    public void Configure(EntityTypeBuilder<Drammers.Modules.Membership.Privacy.PrivacyRequest> builder)
    {
        builder.ToTable("PrivacyRequest", Schemas.Membership);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.FilePath).HasMaxLength(200).IsUnicode(false);
        builder.Property(r => r.SubjectName).HasMaxLength(200);
        builder.HasIndex(r => r.RequestedAt);
        builder.HasIndex(r => r.UserId);
        builder.HasIndex(r => r.ExpiresAt).HasFilter("[file_path] IS NOT NULL");
    }
}

internal sealed class MembershipApplicationConfiguration : IEntityTypeConfiguration<Drammers.Modules.Membership.Applications.MembershipApplication>
{
    public void Configure(EntityTypeBuilder<Drammers.Modules.Membership.Applications.MembershipApplication> builder)
    {
        builder.ToTable("MembershipApplication", Schemas.Membership);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.FirstName).HasMaxLength(50);
        builder.Property(a => a.NamePrefix).HasMaxLength(20);
        builder.Property(a => a.LastName).HasMaxLength(60);
        builder.Property(a => a.Gender).HasMaxLength(1).IsUnicode(false).IsFixedLength();
        builder.Property(a => a.AddressLine).HasMaxLength(150);
        builder.Property(a => a.PostalCode).HasMaxLength(10);
        builder.Property(a => a.City).HasMaxLength(50);
        builder.Property(a => a.Email).HasMaxLength(150);
        builder.Property(a => a.Phone).HasMaxLength(30);
        builder.Property(a => a.GuardianName).HasMaxLength(100);
        builder.Property(a => a.GuardianEmail).HasMaxLength(150);
        builder.Property(a => a.GuardianPhone).HasMaxLength(30);
        builder.Property(a => a.Iban).HasMaxLength(34).IsUnicode(false);
        builder.Property(a => a.AccountHolder).HasMaxLength(100);
        builder.Property(a => a.MandateReference).HasMaxLength(35).IsUnicode(false);
        builder.HasIndex(a => a.MandateReference).IsUnique();
        builder.Property(a => a.VerificationCodeHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(a => a.RejectionReason).HasMaxLength(500);
        builder.Property(a => a.InternalNotes).HasMaxLength(2000);
        builder.Property(a => a.IpHash).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(a => new { a.Status, a.SubmittedAt });
        builder.HasOne<Member>().WithMany().HasForeignKey(a => a.ResultingMemberId).OnDelete(DeleteBehavior.SetNull);
        builder.Ignore(a => a.FullName);
    }
}

internal sealed class GuardianRelationConfiguration : IEntityTypeConfiguration<Drammers.Modules.Membership.Guardians.GuardianRelation>
{
    public void Configure(EntityTypeBuilder<Drammers.Modules.Membership.Guardians.GuardianRelation> builder)
    {
        builder.ToTable("GuardianRelation", Schemas.Membership);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.GuardianName).HasMaxLength(100);
        builder.Property(g => g.GuardianPhone).HasMaxLength(30);
        builder.HasIndex(g => new { g.MemberId, g.GuardianUserId }).IsUnique();
        builder.HasIndex(g => g.GuardianUserId);
        builder.HasOne<Member>().WithMany().HasForeignKey(g => g.MemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Drammers.Modules.Identity.Users.User>().WithMany().HasForeignKey(g => g.GuardianUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
