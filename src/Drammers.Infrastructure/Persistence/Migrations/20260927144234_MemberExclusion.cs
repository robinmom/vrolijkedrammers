using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemberExclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SyncJobItem_action",
                schema: "import",
                table: "SyncJobItem");

            migrationBuilder.CreateTable(
                name: "ExcludedMember",
                schema: "membership",
                columns: table => new
                {
                    member_number = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    excluded_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    excluded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcludedMember", x => x.member_number);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SyncJobItem_action",
                schema: "import",
                table: "SyncJobItem",
                sql: "[action] IN ('Created', 'Updated', 'Unchanged', 'Missing', 'Deactivated', 'Reactivated', 'Warning', 'Error', 'Conflict', 'Excluded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExcludedMember",
                schema: "membership");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SyncJobItem_action",
                schema: "import",
                table: "SyncJobItem");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SyncJobItem_action",
                schema: "import",
                table: "SyncJobItem",
                sql: "[action] IN ('Created', 'Updated', 'Unchanged', 'Missing', 'Deactivated', 'Reactivated', 'Warning', 'Error', 'Conflict')");
        }
    }
}
