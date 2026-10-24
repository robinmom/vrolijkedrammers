using Drammers.Modules.Membership.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class CollectiveInvoiceConfiguration : IEntityTypeConfiguration<CollectiveInvoice>
{
    public void Configure(EntityTypeBuilder<CollectiveInvoice> builder)
    {
        builder.ToTable("CollectiveInvoice", Schemas.Payments);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Number).HasMaxLength(30).IsUnicode(false);
        builder.HasIndex(i => i.Number).IsUnique();
        builder.HasIndex(i => new { i.CarnivalYearId, i.Source, i.Sequence }).IsUnique();
        builder.Property(i => i.Total).HasPrecision(11, 2);
        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CollectiveInvoiceLineConfiguration : IEntityTypeConfiguration<CollectiveInvoiceLine>
{
    public void Configure(EntityTypeBuilder<CollectiveInvoiceLine> builder)
    {
        builder.ToTable("CollectiveInvoiceLine", Schemas.Payments);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.TransactionKey).HasMaxLength(80).IsUnicode(false);
        builder.Property(l => l.Party).HasMaxLength(200);
        builder.Property(l => l.Reference).HasMaxLength(60);
        builder.Property(l => l.Amount).HasPrecision(9, 2);
        builder.HasIndex(l => l.InvoiceId);
    }
}
