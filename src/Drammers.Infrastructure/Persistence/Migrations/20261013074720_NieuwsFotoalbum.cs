using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NieuwsFotoalbum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "photo_album_id",
                schema: "content",
                table: "News",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_News_photo_album_id",
                schema: "content",
                table: "News",
                column: "photo_album_id");

            migrationBuilder.AddForeignKey(
                name: "FK_News_PhotoAlbum_photo_album_id",
                schema: "content",
                table: "News",
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
                name: "FK_News_PhotoAlbum_photo_album_id",
                schema: "content",
                table: "News");

            migrationBuilder.DropIndex(
                name: "IX_News_photo_album_id",
                schema: "content",
                table: "News");

            migrationBuilder.DropColumn(
                name: "photo_album_id",
                schema: "content",
                table: "News");
        }
    }
}
