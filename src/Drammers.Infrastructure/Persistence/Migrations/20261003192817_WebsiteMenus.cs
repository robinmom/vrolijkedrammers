using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebsiteMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WebsiteImportItem_kind",
                schema: "content",
                table: "WebsiteImportItem");

            migrationBuilder.AddColumn<string>(
                name: "menu",
                schema: "content",
                table: "WebsitePage",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "None");

            // De pagina's die tot nu toe vast in het menu Vereniging stonden, op hun oude plek rond Kader (10) t/m Onderscheidingen (40).
            // Via EXEC: in het idempotente script bestaat de nieuwe kolom nog niet als het blok gecompileerd wordt.
            migrationBuilder.Sql("""
                EXEC('UPDATE [content].[WebsitePage] SET [menu] = ''Association'', [sort_order] = CASE [slug]
                    WHEN ''over-ons'' THEN 0 WHEN ''dansgarde'' THEN 50 WHEN ''historie'' THEN 60 ELSE 70 END
                WHERE [slug] IN (''over-ons'', ''dansgarde'', ''historie'', ''loillands'')');
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "photo_album_id",
                schema: "content",
                table: "WebsitePage",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebsitePage_photo_album_id",
                schema: "content",
                table: "WebsitePage",
                column: "photo_album_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebsitePage_menu",
                schema: "content",
                table: "WebsitePage",
                sql: "[menu] IN ('None', 'Association', 'Carnival')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebsiteImportItem_kind",
                schema: "content",
                table: "WebsiteImportItem",
                sql: "[kind] IN ('Post', 'Page', 'Gallery', 'GalleryPhoto', 'Prince', 'YouthPrince', 'Award', 'Kader', 'CarnivalPage')");

            migrationBuilder.AddForeignKey(
                name: "FK_WebsitePage_PhotoAlbum_photo_album_id",
                schema: "content",
                table: "WebsitePage",
                column: "photo_album_id",
                principalSchema: "content",
                principalTable: "PhotoAlbum",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebsitePage_PhotoAlbum_photo_album_id",
                schema: "content",
                table: "WebsitePage");

            migrationBuilder.DropIndex(
                name: "IX_WebsitePage_photo_album_id",
                schema: "content",
                table: "WebsitePage");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebsitePage_menu",
                schema: "content",
                table: "WebsitePage");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WebsiteImportItem_kind",
                schema: "content",
                table: "WebsiteImportItem");

            migrationBuilder.DropColumn(
                name: "menu",
                schema: "content",
                table: "WebsitePage");

            migrationBuilder.DropColumn(
                name: "photo_album_id",
                schema: "content",
                table: "WebsitePage");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WebsiteImportItem_kind",
                schema: "content",
                table: "WebsiteImportItem",
                sql: "[kind] IN ('Post', 'Page', 'Gallery', 'GalleryPhoto', 'Prince', 'YouthPrince', 'Award', 'Kader')");
        }
    }
}
