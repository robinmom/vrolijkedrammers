using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Takenlijst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskCategory",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCategory", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "CarnivalTask",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    category_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    progress = table.Column<int>(type: "int", nullable: false),
                    started_on = table.Column<DateOnly>(type: "date", nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: true),
                    completed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    copied_from_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarnivalTask", x => x.id);
                    table.CheckConstraint("CK_CarnivalTask_progress", "[progress] >= 0 AND [progress] <= 100");
                    table.CheckConstraint("CK_CarnivalTask_status", "[status] IN ('NotStarted', 'InProgress', 'Done', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_CarnivalTask_CarnivalYear_carnival_year_id",
                        column: x => x.carnival_year_id,
                        principalSchema: "content",
                        principalTable: "CarnivalYear",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarnivalTask_TaskCategory_category_id",
                        column: x => x.category_id,
                        principalSchema: "content",
                        principalTable: "TaskCategory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarnivalTaskAssignee",
                schema: "content",
                columns: table => new
                {
                    task_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarnivalTaskAssignee", x => new { x.task_id, x.member_id });
                    table.ForeignKey(
                        name: "FK_CarnivalTaskAssignee_CarnivalTask_task_id",
                        column: x => x.task_id,
                        principalSchema: "content",
                        principalTable: "CarnivalTask",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CarnivalTaskAssignee_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[] { 53, "Beheer", "task.manage", "Takenlijst carnaval: taken, categorieën, voortgang en rapportage" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[] { 53, 11 });

            migrationBuilder.CreateIndex(
                name: "IX_CarnivalTask_carnival_year_id_category_id",
                schema: "content",
                table: "CarnivalTask",
                columns: new[] { "carnival_year_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "IX_CarnivalTask_category_id",
                schema: "content",
                table: "CarnivalTask",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_CarnivalTaskAssignee_member_id",
                schema: "content",
                table: "CarnivalTaskAssignee",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCategory_name",
                schema: "content",
                table: "TaskCategory",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CarnivalTaskAssignee",
                schema: "content");

            migrationBuilder.DropTable(
                name: "CarnivalTask",
                schema: "content");

            migrationBuilder.DropTable(
                name: "TaskCategory",
                schema: "content");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 53, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 53);
        }
    }
}
