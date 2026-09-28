using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemberTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ticketing");

            migrationBuilder.CreateTable(
                name: "Ticket",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    public_ref = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false),
                    credential_version = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    blocked_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    bound_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    bound_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    rebind_count = table.Column<int>(type: "int", nullable: false),
                    bind_challenge_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    bind_challenge_expires_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ticket", x => x.id);
                    table.CheckConstraint("CK_Ticket_public_ref", "DATALENGTH([public_ref]) = 16");
                    table.CheckConstraint("CK_Ticket_status", "[status] IN ('Active', 'Blocked')");
                    table.ForeignKey(
                        name: "FK_Ticket_CarnivalYear_carnival_year_id",
                        column: x => x.carnival_year_id,
                        principalSchema: "content",
                        principalTable: "CarnivalYear",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ticket_Device_bound_device_id",
                        column: x => x.bound_device_id,
                        principalSchema: "identity",
                        principalTable: "Device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Ticket_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketSigningKey",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    public_key = table.Column<byte[]>(type: "varbinary(200)", maxLength: 200, nullable: false),
                    protected_private_key = table.Column<string>(type: "varchar(2000)", unicode: false, maxLength: 2000, nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketSigningKey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ticket_bound_device_id",
                schema: "ticketing",
                table: "Ticket",
                column: "bound_device_id");

            migrationBuilder.CreateIndex(
                name: "IX_Ticket_carnival_year_id_member_id",
                schema: "ticketing",
                table: "Ticket",
                columns: new[] { "carnival_year_id", "member_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ticket_member_id",
                schema: "ticketing",
                table: "Ticket",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_Ticket_public_ref",
                schema: "ticketing",
                table: "Ticket",
                column: "public_ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketSigningKey_active",
                schema: "ticketing",
                table: "TicketSigningKey",
                column: "active",
                unique: true,
                filter: "[active] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ticket",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "TicketSigningKey",
                schema: "ticketing");
        }
    }
}
