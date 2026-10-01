using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebsiteImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebsiteImportItem",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    source_key = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    source_url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    target_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteImportItem", x => x.id);
                    table.CheckConstraint("CK_WebsiteImportItem_kind", "[kind] IN ('Post', 'Page', 'Gallery', 'GalleryPhoto', 'Prince', 'YouthPrince', 'Award', 'Kader')");
                    table.CheckConstraint("CK_WebsiteImportItem_status", "[status] IN ('Pending', 'Done', 'Skipped', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "WebsiteRedirect",
                schema: "content",
                columns: table => new
                {
                    from_path = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    to_path = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteRedirect", x => x.from_path);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteImportItem_kind_source_key",
                schema: "content",
                table: "WebsiteImportItem",
                columns: new[] { "kind", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebsiteImportItem_status_id",
                schema: "content",
                table: "WebsiteImportItem",
                columns: new[] { "status", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebsiteImportItem",
                schema: "content");

            migrationBuilder.DropTable(
                name: "WebsiteRedirect",
                schema: "content");
        }
    }
}
