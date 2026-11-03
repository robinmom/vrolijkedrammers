using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdverteerderVerwijderenEnBeoordelen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "delete_reason",
                schema: "membership",
                table: "Advertiser",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                schema: "membership",
                table: "Advertiser",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by",
                schema: "membership",
                table: "Advertiser",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review",
                schema: "membership",
                table: "Advertiser",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_note",
                schema: "membership",
                table: "Advertiser",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                schema: "membership",
                table: "Advertiser",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "membership",
                table: "Advertiser",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Advertiser_deleted_at",
                schema: "membership",
                table: "Advertiser",
                column: "deleted_at");

            migrationBuilder.CreateIndex(
                name: "IX_Advertiser_review",
                schema: "membership",
                table: "Advertiser",
                column: "review");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Advertiser_review",
                schema: "membership",
                table: "Advertiser",
                sql: "[review] IN ('Pending', 'InfoNeeded', 'Approved')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Advertiser_deleted_at",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropIndex(
                name: "IX_Advertiser_review",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Advertiser_review",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropColumn(
                name: "delete_reason",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropColumn(
                name: "review",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropColumn(
                name: "review_note",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "membership",
                table: "Advertiser");
        }
    }
}
