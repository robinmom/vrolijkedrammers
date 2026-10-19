using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptochtHeroAfbeelding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "optocht_hero_image_blob_path",
                schema: "content",
                table: "WebsiteSettings",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "content",
                table: "WebsiteSettings",
                keyColumn: "id",
                keyValue: 1,
                column: "optocht_hero_image_blob_path",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "optocht_hero_image_blob_path",
                schema: "content",
                table: "WebsiteSettings");
        }
    }
}
