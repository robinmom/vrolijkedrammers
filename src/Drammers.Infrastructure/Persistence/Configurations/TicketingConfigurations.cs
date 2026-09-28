using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Ticket", Schemas.Ticketing, t =>
            t.HasCheckConstraint("CK_Ticket_public_ref", "DATALENGTH([public_ref]) = 16"));
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.PublicRef).HasMaxLength(16).IsFixedLength();
        builder.Property(t => t.BlockedReason).HasMaxLength(500);
        builder.Property(t => t.BindChallengeHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasIndex(t => t.PublicRef).IsUnique();
        // Idempotente uitgifte: één ledenticket per lid per carnavalsjaar.
        builder.HasIndex(t => new { t.CarnivalYearId, t.MemberId }).IsUnique();
        builder.HasOne<CarnivalYear>().WithMany().HasForeignKey(t => t.CarnivalYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Member>().WithMany().HasForeignKey(t => t.MemberId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>().WithMany().HasForeignKey(t => t.BoundDeviceId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TicketSigningKeyConfiguration : IEntityTypeConfiguration<TicketSigningKey>
{
    public void Configure(EntityTypeBuilder<TicketSigningKey> builder)
    {
        builder.ToTable("TicketSigningKey", Schemas.Ticketing);
        builder.Property(k => k.PublicKey).HasMaxLength(200);
        builder.Property(k => k.ProtectedPrivateKey).HasMaxLength(2000).IsUnicode(false);
        // Eén actieve sleutel tegelijk; bij rotatie verlopen codes van de oude sleutel binnen een minuut.
        builder.HasIndex(k => k.Active).IsUnique().HasFilter("[active] = 1");
    }
}
