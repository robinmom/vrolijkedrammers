using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JuryAantekeningen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JudgingComment",
                schema: "parade",
                columns: table => new
                {
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pass = table.Column<int>(type: "int", nullable: false),
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    written_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JudgingComment", x => new { x.registration_id, x.user_id, x.pass });
                    table.CheckConstraint("CK_JudgingComment_pass", "[pass] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_JudgingComment_ParadeRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "parade",
                        principalTable: "ParadeRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JudgingComment_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JudgingComment_parade_id_user_id",
                schema: "parade",
                table: "JudgingComment",
                columns: new[] { "parade_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_JudgingComment_user_id",
                schema: "parade",
                table: "JudgingComment",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JudgingComment",
                schema: "parade");
        }
    }
}
