using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfflineScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_scan_id",
                schema: "ticketing",
                table: "AccessScan",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "offline",
                schema: "ticketing",
                table: "AccessScan",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "offline_outcome",
                schema: "ticketing",
                table: "AccessScan",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "synced_at",
                schema: "ticketing",
                table: "AccessScan",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessScan_client_scan_id",
                schema: "ticketing",
                table: "AccessScan",
                column: "client_scan_id",
                unique: true,
                filter: "[client_scan_id] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessScan_offline_outcome",
                schema: "ticketing",
                table: "AccessScan",
                sql: "[offline_outcome] IN ('Admitted', 'AdmittedAgain', 'Warning', 'Refused')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessScan_client_scan_id",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessScan_offline_outcome",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropColumn(
                name: "client_scan_id",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropColumn(
                name: "offline",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropColumn(
                name: "offline_outcome",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropColumn(
                name: "synced_at",
                schema: "ticketing",
                table: "AccessScan");
        }
    }
}
