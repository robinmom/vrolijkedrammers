using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResultPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "registration_id",
                schema: "content",
                table: "Photo",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "results_album_id",
                schema: "parade",
                table: "Parade",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photo_registration_id",
                schema: "content",
                table: "Photo",
                column: "registration_id",
                filter: "[registration_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Photo_registration_id",
                schema: "content",
                table: "Photo");

            migrationBuilder.DropColumn(
                name: "registration_id",
                schema: "content",
                table: "Photo");

            migrationBuilder.DropColumn(
                name: "results_album_id",
                schema: "parade",
                table: "Parade");
        }
    }
}
