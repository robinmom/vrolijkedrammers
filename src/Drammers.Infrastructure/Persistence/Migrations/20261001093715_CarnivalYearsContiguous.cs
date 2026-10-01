using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarnivalYearsContiguous : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fase 21g: carnavalsjaren sluiten op elkaar aan. Elk jaar begint de dag na het einde van het vorige jaar,
            // zolang dat niet ná de carnavalsdagen van dat jaar valt (dan blijft het zoals het was).
            migrationBuilder.Sql("""
                WITH ordered AS (
                    SELECT id, LAG(end_date) OVER (ORDER BY start_date) AS previous_end
                    FROM content.CarnivalYear)
                UPDATE y
                SET start_date = DATEADD(day, 1, o.previous_end)
                FROM content.CarnivalYear AS y
                JOIN ordered AS o ON o.id = y.id
                WHERE o.previous_end IS NOT NULL
                  AND y.start_date <> DATEADD(day, 1, o.previous_end)
                  AND DATEADD(day, 1, o.previous_end) <= y.carnival_start_date;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Alleen gegevens aangepast; de oude begindatums worden niet teruggezet.
        }
    }
}
