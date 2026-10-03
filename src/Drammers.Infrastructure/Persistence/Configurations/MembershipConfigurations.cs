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
        builder.Property(m => m.ParadeGroupName).HasMaxLength(100);
        builder.Property(m => m.SecondMemberName).HasMaxLength(150);
        builder.Property(m => m.JubileeNote).HasMaxLength(200);
        builder.Property(m => m.LocalFields).HasMaxLength(400);
        builder.Property(m => m.MembershipKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.ContributionExemptReason).HasMaxLength(200);
        builder.HasOne<Member>().WithMany().HasForeignKey(m => m.PayerMemberId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(m => m.PayerMemberId).IsUnique().HasFilter("[payer_member_id] IS NOT NULL");
        builder.Ignore(m => m.JubileeBaseYear);
        builder.Property(m => m.FirstName).HasMaxLength(100);
        builder.Property(m => m.NamePrefix).HasMaxLength(30);
        builder.Property(m => m.LastName).HasMaxLength(100);
        builder.Property(m => m.EbHash).HasMaxLength(32).IsFixedLength();
        builder.Ignore(m => m.EffectiveStatus);
        builder.HasIndex(m => new { m.MembershipStatus, m.LastName });
    }
}

internal sealed class MembershipSplitInvitationConfiguration : IEntityTypeConfiguration<MembershipSplitInvitation>
{
    public void Configure(EntityTypeBuilder<MembershipSplitInvitation> builder)
    {
        builder.ToTable("MembershipSplitInvitation", Schemas.Membership);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.TokenHash).HasMaxLength(64);
        builder.Property(i => i.SentTo).HasMaxLength(254);
        builder.HasIndex(i => i.MemberId).IsUnique();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasOne<Member>().WithMany().HasForeignKey(i => i.MemberId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContributionRateConfiguration : IEntityTypeConfiguration<ContributionRate>
{
    public void Configure(EntityTypeBuilder<ContributionRate> builder)
    {
        builder.ToTable("ContributionRate", Schemas.Membership);
        builder.HasIndex(r => r.ValidFrom).IsUnique();
        foreach (var amount in new[] { "OnePerson", "TwoPersons", "OnePersonSenior", "TwoPersonsSenior", "Dansgarde" })
        {
            builder.Property<decimal>(amount).HasPrecision(9, 2);
        }

        // Tarieven 2026 (bestuur, 2026-10-03).
        builder.HasData(new ContributionRate
        {
            Id = 1,
            ValidFrom = new DateOnly(2026, 1, 1),
            OnePerson = 32.50m,
            TwoPersons = 57.50m,
            OnePersonSenior = 22.00m,
            TwoPersonsSenior = 44.00m,
            Dansgarde = 85.00m,
        });
    }
}

internal sealed class JubileeInvitationConfiguration : IEntityTypeConfiguration<JubileeInvitation>
{
    public void Configure(EntityTypeBuilder<JubileeInvitation> builder)
    {
        builder.ToTable("JubileeInvitation", Schemas.Membership);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.SentTo).HasMaxLength(254);
        builder.HasIndex(i => new { i.MemberId, i.CarnivalYearId }).IsUnique();
        builder.HasOne<Member>().WithMany().HasForeignKey(i => i.MemberId).OnDelete(DeleteBehavior.Cascade);
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

internal sealed class GuardianLinkRequestConfiguration : IEntityTypeConfiguration<Drammers.Modules.Membership.Guardians.GuardianLinkRequest>
{
    public void Configure(EntityTypeBuilder<Drammers.Modules.Membership.Guardians.GuardianLinkRequest> builder)
    {
        builder.ToTable("GuardianLinkRequest", Schemas.Membership);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ChildFirstName).HasMaxLength(50);
        builder.Property(r => r.ChildLastName).HasMaxLength(80);
        builder.Property(r => r.Phone).HasMaxLength(30);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => r.RequestedByUserId);
        builder.HasOne<Drammers.Modules.Identity.Users.User>().WithMany().HasForeignKey(r => r.RequestedByUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Member>().WithMany().HasForeignKey(r => r.MemberId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class GuardianSuggestionDismissalConfiguration : IEntityTypeConfiguration<Drammers.Modules.Membership.Guardians.GuardianSuggestionDismissal>
{
    public void Configure(EntityTypeBuilder<Drammers.Modules.Membership.Guardians.GuardianSuggestionDismissal> builder)
    {
        builder.ToTable("GuardianSuggestionDismissal", Schemas.Membership);
        builder.HasKey(d => new { d.ChildMemberId, d.ParentMemberId });
        builder.HasOne<Member>().WithMany().HasForeignKey(d => d.ChildMemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Member>().WithMany().HasForeignKey(d => d.ParentMemberId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ExcludedMemberConfiguration : IEntityTypeConfiguration<ExcludedMember>
{
    public void Configure(EntityTypeBuilder<ExcludedMember> builder)
    {
        builder.ToTable("ExcludedMember", Schemas.Membership);
        builder.HasKey(e => e.MemberNumber);
        builder.Property(e => e.MemberNumber).HasMaxLength(15);
    }
}
