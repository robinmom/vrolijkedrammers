using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FestivalTakenlijst : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskCategory_name",
                schema: "content",
                table: "TaskCategory");

            migrationBuilder.AddColumn<string>(
                name: "list",
                schema: "content",
                table: "TaskCategory",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "Carnival");

            migrationBuilder.AddColumn<string>(
                name: "other_people",
                schema: "content",
                table: "CarnivalTask",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskCategory_list_name",
                schema: "content",
                table: "TaskCategory",
                columns: new[] { "list", "name" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskCategory_list",
                schema: "content",
                table: "TaskCategory",
                sql: "[list] IN ('Carnival', 'Festival')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskCategory_list_name",
                schema: "content",
                table: "TaskCategory");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskCategory_list",
                schema: "content",
                table: "TaskCategory");

            migrationBuilder.DropColumn(
                name: "list",
                schema: "content",
                table: "TaskCategory");

            migrationBuilder.DropColumn(
                name: "other_people",
                schema: "content",
                table: "CarnivalTask");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCategory_name",
                schema: "content",
                table: "TaskCategory",
                column: "name",
                unique: true);
        }
    }
}
