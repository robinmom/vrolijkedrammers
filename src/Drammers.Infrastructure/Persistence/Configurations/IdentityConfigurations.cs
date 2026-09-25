using Drammers.Modules.Identity.Provisioning;
using Drammers.Modules.Identity.Roles;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using P = Drammers.SharedKernel.Authorization.Permissions;

namespace Drammers.Infrastructure.Persistence.Configurations;

/// <summary>
/// Standaardrollen en hun permissions (docs/07 §3). Scoped permissions (◐) worden aan de rol gegeven; de scoping
/// (eigen inschrijving, eigen kinderen, eigen groep) volgt met resource-handlers in de betreffende fase. Uitzonderingen
/// die alleen voor één persoon gelden (◐ voorzitter, ◐ penningmeester) worden niet standaard toegekend.
/// </summary>
public static class DefaultRoles
{
    public const string Lid = "lid";
    public const string Groepsverantwoordelijke = "groepsverantwoordelijke";
    public const string Kaderlid = "kaderlid";
    public const string DansgardeLeiding = "dansgarde-leiding";
    public const string DansgardeLid = "dansgarde-lid";
    public const string Ouder = "ouder";
    public const string RaadVanElf = "raad-van-elf";
    public const string Scanner = "scanner";
    public const string Optochtcommissie = "optochtcommissie";
    public const string Redactie = "redactie";
    public const string Bestuur = "bestuur";
    public const string BeheerderIt = "beheerder-it";

    private static readonly string[] MemberBasics =
        [P.MemberReadOwn, P.EventRead, P.NewsRead, P.PhotoRead, P.NotificationReadOwn, P.TicketReadOwn];

    public static readonly IReadOnlyList<RoleDefinition> All =
    [
        new(1, Lid, "Carnavalist", "Lid van de vereniging (systeemrol)", IsSystem: true, IsAssignableBySync: true,
            [.. MemberBasics, P.ParadeRegister]),
        new(2, Groepsverantwoordelijke, "Groepsverantwoordelijke", "Beheert eigen optochtinschrijvingen", false, true,
            [P.NotificationReadOwn, P.ParadeRegister, P.ParadeUpdate]),
        new(3, Kaderlid, "Kaderlid", "Kader; vooral via doelgroepen", false, true, MemberBasics),
        new(4, DansgardeLeiding, "Dansgarde leiding", "Leiding van de dansgarde", false, true,
            [.. MemberBasics, P.NotificationSendGroup]),
        new(5, DansgardeLid, "Dansgarde lid", "Lid van de dansgarde", false, true, MemberBasics),
        new(6, Ouder, "Ouder/verzorger", "Ouder of verzorger van een minderjarig lid (systeemrol)", IsSystem: true, IsAssignableBySync: true,
            [P.GuardianReadOwn, P.NotificationReadOwn, P.TicketReadOwn]),
        new(7, RaadVanElf, "Raad van Elf", "Raad van Elf", false, true, [.. MemberBasics, P.ReportView]),
        new(8, Scanner, "Scanner", "Mag scannen op een trusted device (eventueel tijdelijk)", false, false,
            [.. MemberBasics, P.TicketScan]),
        new(9, Optochtcommissie, "Optochtcommissie", "Organisatie van de optocht", false, false,
            [.. MemberBasics, P.NotificationSendGroup, P.ParadeRead, P.ParadeManage, P.ParadeAssignStartNumber,
             P.ParadeImportArrivalTimes, P.ParadeExport, P.ParadeConfig, P.ReportView]),
        new(10, Redactie, "Redactie", "Nieuws, agenda en foto's", false, false,
            [.. MemberBasics, P.EventManage, P.NewsManage, P.PhotoManage, P.NotificationSend]),
        new(11, Bestuur, "Bestuur", "Bestuur van de vereniging (systeemrol)", IsSystem: true, IsAssignableBySync: false,
            [.. MemberBasics, P.MemberRead, P.MemberUpdate, P.MemberApprove, P.MemberExport, P.MemberBlock, P.MemberPrivacy,
             P.EventManage, P.NewsManage, P.PhotoManage, P.NotificationSend, P.NotificationSendUrgent, P.ParadeRegister,
             P.ParadeRead, P.ParadeManage, P.ParadeManageFinal, P.ParadeAssignStartNumber, P.ParadeImportArrivalTimes,
             P.ParadeExport, P.ParadeConfig, P.TicketScan, P.TicketScanDetails, P.TicketRead, P.TicketManage,
             P.PaymentRead, P.ReportView, P.ImportRun, P.AuditRead, P.RoleManage, P.ConfigManage, P.MemberPurge]),
        new(12, BeheerderIt, "Beheerder (IT)", "Technisch beheer, zonder inhoudelijke rechten op betalingen en goedkeuringen (systeemrol)",
            IsSystem: true, IsAssignableBySync: false,
            [.. MemberBasics, P.MemberRead, P.MemberUpdate, P.MemberBlock, P.ImportRun, P.AuditRead, P.RoleManage, P.ConfigManage, P.MemberPurge]),
    ];

    /// <summary>Vast Id per permission (volgorde in de catalogus, vanaf 1); nieuwe permissions achteraan toevoegen.</summary>
    public static int PermissionId(string code) => IndexOf(code) + 1;

    private static int IndexOf(string code)
    {
        for (var i = 0; i < P.All.Count; i++)
        {
            if (P.All[i].Code == code)
            {
                return i;
            }
        }

        throw new ArgumentException($"Onbekende permission '{code}'", nameof(code));
    }
}

public sealed record RoleDefinition(
    int Id, string Code, string Name, string Description, bool IsSystem, bool IsAssignableBySync, IReadOnlyList<string> Permissions);

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("User", Schemas.Identity);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.ExternalObjectId).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(u => u.ExternalObjectId).IsUnique();
        builder.Property(u => u.Email).HasMaxLength(254);
        builder.HasIndex(u => u.Email);
        builder.Property(u => u.DisplayName).HasMaxLength(200);
        builder.HasIndex(u => u.MemberId).IsUnique().HasFilter("[member_id] IS NOT NULL");
        builder.HasOne<Modules.Membership.Members.Member>().WithMany().HasForeignKey(u => u.MemberId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(u => u.PermissionsVersion).IsConcurrencyToken();
        builder.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRole", Schemas.Identity);
        builder.HasKey(r => new { r.UserId, r.RoleId });
        builder.HasOne<Role>().WithMany().HasForeignKey(r => r.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Role", Schemas.Identity);
        builder.Property(r => r.Code).HasMaxLength(50).IsUnicode(false);
        builder.HasIndex(r => r.Code).IsUnique();
        builder.Property(r => r.Name).HasMaxLength(100);
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasData(DefaultRoles.All.Select((r, i) => new Role
        {
            Id = r.Id,
            Code = r.Code,
            Name = r.Name,
            Description = r.Description,
            IsSystem = r.IsSystem,
            IsAssignableBySync = r.IsAssignableBySync,
            SortOrder = (i + 1) * 10,
        }));
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permission", Schemas.Identity);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Code).HasMaxLength(80).IsUnicode(false);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Description).HasMaxLength(300);
        builder.Property(p => p.Category).HasMaxLength(50);

        builder.HasData(Permissions.All.Select(p => new Permission
        {
            Id = DefaultRoles.PermissionId(p.Code),
            Code = p.Code,
            Description = p.Description,
            Category = p.Category,
        }));
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermission", Schemas.Identity);
        builder.HasKey(p => new { p.RoleId, p.PermissionId });
        builder.HasOne<Permission>().WithMany().HasForeignKey(p => p.PermissionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasData(DefaultRoles.All.SelectMany(role => role.Permissions.Distinct().Select(code => new RolePermission
        {
            RoleId = role.Id,
            PermissionId = DefaultRoles.PermissionId(code),
        })));
    }
}

internal sealed class LoginHistoryConfiguration : IEntityTypeConfiguration<LoginHistory>
{
    public void Configure(EntityTypeBuilder<LoginHistory> builder)
    {
        builder.ToTable("LoginHistory", Schemas.Identity);
        builder.Property(l => l.Id).UseIdentityColumn();
        builder.Property(l => l.SubjectHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(l => l.Reason).HasMaxLength(100).IsUnicode(false);
        builder.Property(l => l.IpHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(l => l.UserAgent).HasMaxLength(300);
        builder.HasIndex(l => new { l.UserId, l.OccurredAt });
        builder.HasIndex(l => l.OccurredAt);
    }
}

internal sealed class AccountProvisioningConfiguration : IEntityTypeConfiguration<AccountProvisioning>
{
    public void Configure(EntityTypeBuilder<AccountProvisioning> builder)
    {
        builder.ToTable("AccountProvisioning", Schemas.Identity);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.SourceId).HasMaxLength(254);
        builder.HasIndex(p => new { p.SourceType, p.SourceId }).IsUnique();
        builder.Property(p => p.EbMemberId).HasMaxLength(50).IsUnicode(false);
        builder.Property(p => p.MemberNumber).HasMaxLength(20).IsUnicode(false);
        builder.Property(p => p.EntraObjectId).HasMaxLength(64).IsUnicode(false);
        builder.Property(p => p.LastError).HasMaxLength(2000);
        builder.Property(p => p.RowVersion).IsRowVersion();
    }
}
