using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JudgingScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JudgingOutsideReview",
                schema: "parade",
                columns: table => new
                {
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decision = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JudgingOutsideReview", x => new { x.parade_id, x.user_id, x.registration_id });
                    table.CheckConstraint("CK_JudgingOutsideReview_decision", "[decision] IN ('Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_JudgingOutsideReview_ParadeRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "parade",
                        principalTable: "ParadeRegistration",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_JudgingOutsideReview_Parade_parade_id",
                        column: x => x.parade_id,
                        principalSchema: "parade",
                        principalTable: "Parade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JudgingScore",
                schema: "parade",
                columns: table => new
                {
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    pass = table.Column<int>(type: "int", nullable: false),
                    criterion = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    value = table.Column<int>(type: "int", nullable: false),
                    scored_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JudgingScore", x => new { x.registration_id, x.user_id, x.pass, x.criterion });
                    table.CheckConstraint("CK_JudgingScore_criterion", "[criterion] IN ('Originality', 'Carnivalesque', 'Quality', 'Overall')");
                    table.CheckConstraint("CK_JudgingScore_pass", "[pass] BETWEEN 1 AND 3");
                    table.CheckConstraint("CK_JudgingScore_value", "[value] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_JudgingScore_ParadeRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "parade",
                        principalTable: "ParadeRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JudgingScore_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JudgingSubmission",
                schema: "parade",
                columns: table => new
                {
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JudgingSubmission", x => new { x.parade_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_JudgingSubmission_Parade_parade_id",
                        column: x => x.parade_id,
                        principalSchema: "parade",
                        principalTable: "Parade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JudgingSubmission_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JudgingOutsideReview_registration_id",
                schema: "parade",
                table: "JudgingOutsideReview",
                column: "registration_id");

            migrationBuilder.CreateIndex(
                name: "IX_JudgingScore_parade_id_user_id",
                schema: "parade",
                table: "JudgingScore",
                columns: new[] { "parade_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_JudgingScore_user_id",
                schema: "parade",
                table: "JudgingScore",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_JudgingSubmission_user_id",
                schema: "parade",
                table: "JudgingSubmission",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JudgingOutsideReview",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "JudgingScore",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "JudgingSubmission",
                schema: "parade");
        }
    }
}
