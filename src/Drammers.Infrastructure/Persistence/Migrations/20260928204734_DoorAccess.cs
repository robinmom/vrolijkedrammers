using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DoorAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "access_control",
                schema: "content",
                table: "Event",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AccessScan",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    method = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    outcome = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    decision = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    operator_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operator_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    scanned_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    decided_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessScan", x => x.id);
                    table.CheckConstraint("CK_AccessScan_decision", "[decision] IN ('Admitted', 'Refused')");
                    table.CheckConstraint("CK_AccessScan_method", "[method] IN ('Qr', 'Manual')");
                    table.CheckConstraint("CK_AccessScan_outcome", "[outcome] IN ('Admitted', 'AdmittedAgain', 'Warning', 'Refused')");
                    table.ForeignKey(
                        name: "FK_AccessScan_Event_event_id",
                        column: x => x.event_id,
                        principalSchema: "content",
                        principalTable: "Event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessScan_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 8,
                columns: new[] { "description", "name" },
                values: new object[] { "Mag bij activiteiten met toegangscontrole QR-codes scannen en leden inchecken", "Deurcontrole" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[] { 2, 8 });

            migrationBuilder.CreateIndex(
                name: "IX_AccessScan_event_id_member_id",
                schema: "ticketing",
                table: "AccessScan",
                columns: new[] { "event_id", "member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessScan_event_id_scanned_at",
                schema: "ticketing",
                table: "AccessScan",
                columns: new[] { "event_id", "scanned_at" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessScan_member_id",
                schema: "ticketing",
                table: "AccessScan",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_AccessScan_ticket_id",
                schema: "ticketing",
                table: "AccessScan",
                column: "ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessScan",
                schema: "ticketing");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 2, 8 });

            migrationBuilder.DropColumn(
                name: "access_control",
                schema: "content",
                table: "Event");

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 8,
                columns: new[] { "description", "name" },
                values: new object[] { "Mag scannen op een trusted device (eventueel tijdelijk)", "Scanner" });
        }
    }
}
