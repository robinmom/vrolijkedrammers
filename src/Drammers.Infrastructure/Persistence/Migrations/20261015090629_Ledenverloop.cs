using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Ledenverloop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemberExit",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_number = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    reason = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    exited_on = table.Column<DateOnly>(type: "date", nullable: false),
                    carnival_year = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberExit", x => x.id);
                    table.CheckConstraint("CK_MemberExit_reason", "[reason] IN ('Inactive', 'Deceased', 'Suspended', 'Removed')");
                });

            migrationBuilder.CreateTable(
                name: "MemberYearStat",
                schema: "membership",
                columns: table => new
                {
                    carnival_year = table.Column<int>(type: "int", nullable: false),
                    snapshot_total = table.Column<int>(type: "int", nullable: true),
                    snapshot_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    manual_new = table.Column<int>(type: "int", nullable: true),
                    manual_left = table.Column<int>(type: "int", nullable: true),
                    manual_total = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberYearStat", x => x.carnival_year);
                    table.CheckConstraint("CK_MemberYearStat_values", "([manual_new] IS NULL OR [manual_new] >= 0) AND ([manual_left] IS NULL OR [manual_left] >= 0) AND ([manual_total] IS NULL OR [manual_total] >= 0)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberExit_carnival_year",
                schema: "membership",
                table: "MemberExit",
                column: "carnival_year");

            migrationBuilder.CreateIndex(
                name: "IX_MemberExit_member_number",
                schema: "membership",
                table: "MemberExit",
                column: "member_number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberExit",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "MemberYearStat",
                schema: "membership");
        }
    }
}
