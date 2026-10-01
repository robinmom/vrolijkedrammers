using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FotoGalerijen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                schema: "content",
                table: "PhotoAlbum",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                // Bestaande albums krijgen de soort Overig; het bestuur kan ze daarna indelen.
                defaultValue: "Other");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PhotoAlbum_category",
                schema: "content",
                table: "PhotoAlbum",
                sql: "[category] IN ('Pronkzitting', 'Carnival', 'Parade', 'Dansgarde', 'Youth', 'Events', 'Other')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PhotoAlbum_category",
                schema: "content",
                table: "PhotoAlbum");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "content",
                table: "PhotoAlbum");
        }
    }
}
