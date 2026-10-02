using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JubileeInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JubileeInvitation",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    years = table.Column<int>(type: "int", nullable: false),
                    sent_to = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    invited_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    invited_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    email_sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JubileeInvitation", x => x.id);
                    table.ForeignKey(
                        name: "FK_JubileeInvitation_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JubileeInvitation_member_id_carnival_year_id",
                schema: "membership",
                table: "JubileeInvitation",
                columns: new[] { "member_id", "carnival_year_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JubileeInvitation",
                schema: "membership");
        }
    }
}
