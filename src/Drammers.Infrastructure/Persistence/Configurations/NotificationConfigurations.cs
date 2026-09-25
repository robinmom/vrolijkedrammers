using Drammers.Modules.Notification.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("Outbox", Schemas.Notification);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(100).IsUnicode(false);
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // Alleen onverwerkte berichten worden gepolld.
        builder.HasIndex(m => m.CreatedAt).HasFilter("[processed_at] IS NULL");
    }
}
