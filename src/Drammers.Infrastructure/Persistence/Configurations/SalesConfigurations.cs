using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Content.Events;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class SaleProductConfiguration : IEntityTypeConfiguration<SaleProduct>
{
    public void Configure(EntityTypeBuilder<SaleProduct> builder)
    {
        builder.ToTable("SaleProduct", Schemas.Ticketing, t =>
        {
            t.HasCheckConstraint("CK_SaleProduct_price", "[price_cents] >= 0");
            t.HasCheckConstraint("CK_SaleProduct_capacity", "[capacity] IS NULL OR [capacity] >= 0");
            t.HasCheckConstraint("CK_SaleProduct_max_per_order", "[max_per_order] BETWEEN 1 AND 500");
        });
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(120);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Ignore(p => p.GroupOrders);
        builder.Ignore(p => p.MembersOnly);
        builder.Ignore(p => p.GuestsOnly);
        builder.HasIndex(p => new { p.CarnivalYearId, p.SortOrder });
        builder.HasOne<CarnivalYear>().WithMany().HasForeignKey(p => p.CarnivalYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(p => p.EventId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SaleOrderConfiguration : IEntityTypeConfiguration<SaleOrder>
{
    public void Configure(EntityTypeBuilder<SaleOrder> builder)
    {
        builder.ToTable("SaleOrder", Schemas.Payments, t =>
        {
            t.HasCheckConstraint("CK_SaleOrder_quantity", "[member_quantity] >= 0 AND [paid_quantity] >= 0 AND [member_quantity] + [paid_quantity] > 0");
            t.HasCheckConstraint("CK_SaleOrder_amount", "[amount_cents] >= 0");
        });
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Number).HasMaxLength(20).IsUnicode(false);
        builder.Property(o => o.GroupName).HasMaxLength(100);
        builder.Property(o => o.BuyerName).HasMaxLength(200);
        builder.Property(o => o.BuyerEmail).HasMaxLength(254);
        builder.Property(o => o.BuyerPhone).HasMaxLength(40);
        builder.Property(o => o.Remark).HasMaxLength(500);
        builder.Property(o => o.AccessTokenProtected).HasMaxLength(500).IsUnicode(false);
        builder.Property(o => o.MolliePaymentId).HasMaxLength(40).IsUnicode(false);
        builder.Property(o => o.CancelReason).HasMaxLength(500);
        builder.Property(o => o.RowVersion).IsRowVersion();
        builder.Ignore(o => o.Quantity);
        builder.HasIndex(o => o.Number).IsUnique();
        builder.HasIndex(o => new { o.ProductId, o.Status });
        builder.HasIndex(o => o.MolliePaymentId).HasFilter("[mollie_payment_id] IS NOT NULL");
        builder.HasIndex(o => o.BuyerUserId);
        builder.HasIndex(o => new { o.CarnivalYearId, o.GroupName });
        builder.HasOne<CarnivalYear>().WithMany().HasForeignKey(o => o.CarnivalYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SaleProduct>().WithMany().HasForeignKey(o => o.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.BuyerUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Member>().WithMany().HasForeignKey(o => o.BuyerMemberId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class SaleOrderSequenceConfiguration : IEntityTypeConfiguration<SaleOrderSequence>
{
    public void Configure(EntityTypeBuilder<SaleOrderSequence> builder)
    {
        builder.ToTable("SaleOrderSequence", Schemas.Payments);
        builder.HasKey(s => s.CarnivalYearId);
        builder.Property(s => s.CarnivalYearId).ValueGeneratedNever();
        builder.HasOne<CarnivalYear>().WithMany().HasForeignKey(s => s.CarnivalYearId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrderTicketConfiguration : IEntityTypeConfiguration<OrderTicket>
{
    public void Configure(EntityTypeBuilder<OrderTicket> builder)
    {
        builder.ToTable("OrderTicket", Schemas.Ticketing, t =>
        {
            t.HasCheckConstraint("CK_OrderTicket_public_ref", "DATALENGTH([public_ref]) = 16");
            t.HasCheckConstraint("CK_OrderTicket_quantity", "[quantity] > 0");
        });
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.PublicRef).HasMaxLength(16).IsFixedLength();
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasIndex(t => t.PublicRef).IsUnique();
        builder.HasIndex(t => t.OrderId);
        // Geen foreign key naar het lid: lid → bestelling → QR zou een tweede verwijderpad geven. Bij het verwijderen van
        // een lid maakt de ledenadministratie de houder leeg (AVG).
        builder.HasIndex(t => t.HolderMemberId);
        builder.HasOne<SaleOrder>().WithMany().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("WaitlistEntry", Schemas.Ticketing, t =>
            t.HasCheckConstraint("CK_WaitlistEntry_quantity", "[member_quantity] >= 0 AND [paid_quantity] >= 0 AND [member_quantity] + [paid_quantity] > 0"));
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.GroupName).HasMaxLength(100);
        builder.Property(w => w.BuyerName).HasMaxLength(200);
        builder.Property(w => w.BuyerEmail).HasMaxLength(254);
        builder.Property(w => w.BuyerPhone).HasMaxLength(40);
        builder.Property(w => w.Remark).HasMaxLength(500);
        builder.Property(w => w.RowVersion).IsRowVersion();
        builder.Ignore(w => w.Quantity);
        builder.HasIndex(w => new { w.ProductId, w.Status, w.CreatedAt });
        builder.HasOne<SaleProduct>().WithMany().HasForeignKey(w => w.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.BuyerUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Member>().WithMany().HasForeignKey(w => w.BuyerMemberId).OnDelete(DeleteBehavior.SetNull);
    }
}
