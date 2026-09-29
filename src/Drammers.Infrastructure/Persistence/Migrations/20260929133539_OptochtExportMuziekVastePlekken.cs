using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptochtExportMuziekVastePlekken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_music",
                schema: "parade",
                table: "ParadeRegistration",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fixed_entries",
                schema: "parade",
                table: "Parade",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "parade",
                table: "ParadeStatusEditPolicy",
                keyColumn: "id",
                keyValue: 2,
                column: "editable_fields",
                value: "contactName,contactPhone,contactEmail,childrenCount,adultCount,hasMusic,additionalInformation,documents,estimatedLengthMeters");

            // Bestaande optochten krijgen de drie vaste plekken, tenzij een groep al startnummer 1, 2 of 3 heeft (dan leeg;
            // de commissie stelt ze in het portal in nadat dat nummer is aangepast).
            migrationBuilder.Sql(
                """
                EXEC(N'UPDATE p SET fixed_entries = CASE WHEN EXISTS (SELECT 1 FROM parade.ParadeRegistration r WHERE r.parade_id = p.id AND r.start_number <= 3) THEN N''[]'' ELSE N''[{"AdultCount":2,"ChildrenCount":0,"HasMusic":true,"Name":"Geluidswagen"},{"AdultCount":14,"ChildrenCount":0,"HasMusic":true,"Name":"Verenigingswagen \u0022de Vrolijke Drammers\u0022"},{"AdultCount":8,"ChildrenCount":0,"HasMusic":false,"Name":"Het Convent van \u0022de Vrolijke Drammers\u0022"}]'' END FROM parade.Parade p WHERE p.fixed_entries IS NULL')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_music",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "fixed_entries",
                schema: "parade",
                table: "Parade");

            migrationBuilder.UpdateData(
                schema: "parade",
                table: "ParadeStatusEditPolicy",
                keyColumn: "id",
                keyValue: 2,
                column: "editable_fields",
                value: "contactName,contactPhone,contactEmail,childrenCount,adultCount,additionalInformation,documents,estimatedLengthMeters");
        }
    }
}
