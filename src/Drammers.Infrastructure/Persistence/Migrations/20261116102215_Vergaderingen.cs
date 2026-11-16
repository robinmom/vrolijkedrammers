using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Vergaderingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Meeting",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    audience = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    intro = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    sender = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    agenda_sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    agenda_sent_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    agenda_recipient_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meeting", x => x.id);
                    table.CheckConstraint("CK_Meeting_audience", "[audience] IN ('Board', 'CouncilOfEleven', 'Both')");
                    table.CheckConstraint("CK_Meeting_sender", "[sender] IN ('Secretary', 'Chairman')");
                    table.ForeignKey(
                        name: "FK_Meeting_Event_event_id",
                        column: x => x.event_id,
                        principalSchema: "content",
                        principalTable: "Event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MeetingAgendaItem",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    meeting_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingAgendaItem", x => x.id);
                    table.ForeignKey(
                        name: "FK_MeetingAgendaItem_Meeting_meeting_id",
                        column: x => x.meeting_id,
                        principalSchema: "content",
                        principalTable: "Meeting",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[] { 58, "Beheer", "meeting.manage", "Vergaderingen: agenda maken, notulen bewaren en de agenda versturen" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[] { 58, 11 });

            migrationBuilder.CreateIndex(
                name: "IX_Meeting_event_id",
                schema: "content",
                table: "Meeting",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeetingAgendaItem_meeting_id_sort_order",
                schema: "content",
                table: "MeetingAgendaItem",
                columns: new[] { "meeting_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeetingAgendaItem",
                schema: "content");

            migrationBuilder.DropTable(
                name: "Meeting",
                schema: "content");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 58, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 58);
        }
    }
}
