using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScannerEnKassa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "order_ticket_id",
                schema: "ticketing",
                table: "AccessScan",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "persons",
                schema: "ticketing",
                table: "AccessScan",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TokenScan",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_ticket_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    quantity = table.Column<int>(type: "int", nullable: true),
                    outcome = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    operator_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operator_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    scanned_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    issued_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenScan", x => x.id);
                    table.CheckConstraint("CK_TokenScan_outcome", "[outcome] IN ('Ready', 'Issued', 'Refused')");
                    table.CheckConstraint("CK_TokenScan_quantity", "[quantity] IS NULL OR [quantity] > 0");
                    table.ForeignKey(
                        name: "FK_TokenScan_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessScan_order_ticket_id",
                schema: "ticketing",
                table: "AccessScan",
                column: "order_ticket_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessScan_persons",
                schema: "ticketing",
                table: "AccessScan",
                sql: "[persons] IS NULL OR [persons] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_TokenScan_member_id",
                schema: "ticketing",
                table: "TokenScan",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_TokenScan_order_ticket_id",
                schema: "ticketing",
                table: "TokenScan",
                column: "order_ticket_id");

            migrationBuilder.CreateIndex(
                name: "IX_TokenScan_scanned_at",
                schema: "ticketing",
                table: "TokenScan",
                column: "scanned_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TokenScan",
                schema: "ticketing");

            migrationBuilder.DropIndex(
                name: "IX_AccessScan_order_ticket_id",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessScan_persons",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropColumn(
                name: "order_ticket_id",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropColumn(
                name: "persons",
                schema: "ticketing",
                table: "AccessScan");
        }
    }
}
