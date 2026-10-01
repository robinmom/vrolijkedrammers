using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Jubilees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "jubilee_join_year_override",
                schema: "membership",
                table: "Member",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "jubilee_note",
                schema: "membership",
                table: "Member",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "jubilee_join_year_override",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "jubilee_note",
                schema: "membership",
                table: "Member");
        }
    }
}
