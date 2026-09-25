using Drammers.Modules.Audit.AuditLog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLog", Schemas.Audit);
        builder.Property(a => a.Id).UseIdentityColumn();
        builder.Property(a => a.Action).HasMaxLength(100).IsUnicode(false);
        builder.Property(a => a.EntityType).HasMaxLength(100).IsUnicode(false);
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.IpHash).HasMaxLength(64).IsUnicode(false);
        builder.Property(a => a.CorrelationId).HasMaxLength(100).IsUnicode(false);
        builder.Property(a => a.HashPrev).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(a => a.Hash).HasMaxLength(64).IsUnicode(false).IsFixedLength();

        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.OccurredAt });
        builder.HasIndex(a => new { a.ActorUserId, a.OccurredAt });
    }
}
