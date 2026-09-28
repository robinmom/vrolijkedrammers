using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParadeSupplementEditable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "parade",
                table: "ParadeStatusEditPolicy",
                keyColumn: "id",
                keyValue: 4,
                column: "editable_fields",
                value: "*");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "parade",
                table: "ParadeStatusEditPolicy",
                keyColumn: "id",
                keyValue: 4,
                column: "editable_fields",
                value: "contactName,contactPhone,contactEmail,childrenCount,adultCount,additionalInformation,documents,estimatedLengthMeters");
        }
    }
}
