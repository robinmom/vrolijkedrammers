using Drammers.Modules.Content.CarnivalYears;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Drammers.Infrastructure.Persistence.Configurations;

internal sealed class CarnivalYearConfiguration : IEntityTypeConfiguration<CarnivalYear>
{
    public void Configure(EntityTypeBuilder<CarnivalYear> builder)
    {
        builder.ToTable("CarnivalYear", Schemas.Content);
        builder.Property(y => y.Name).HasMaxLength(20);
        builder.HasIndex(y => y.Name).IsUnique();

        // Precies één actief carnavalsjaar (docs/04 §5).
        builder.HasIndex(y => y.Active).IsUnique().HasFilter("[active] = 1");

        builder.HasData(new CarnivalYear
        {
            Id = 1,
            Name = "2026/2027",
            StartDate = new DateOnly(2026, 11, 11),
            EndDate = new DateOnly(2027, 2, 10),
            CarnivalStartDate = new DateOnly(2027, 2, 6),
            CarnivalEndDate = new DateOnly(2027, 2, 9),
            Active = true,
        });
    }
}
