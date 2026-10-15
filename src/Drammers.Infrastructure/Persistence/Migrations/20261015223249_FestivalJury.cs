using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FestivalJury : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "awards_completed_at",
                schema: "content",
                table: "DanceFestival",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "awards_completed_by",
                schema: "content",
                table: "DanceFestival",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "head_jury_published_at",
                schema: "content",
                table: "DanceFestival",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FestivalCategorySubmission",
                schema: "content",
                columns: table => new
                {
                    festival_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category_key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FestivalCategorySubmission", x => new { x.festival_id, x.category_key, x.user_id });
                    table.ForeignKey(
                        name: "FK_FestivalCategorySubmission_DanceFestival_festival_id",
                        column: x => x.festival_id,
                        principalSchema: "content",
                        principalTable: "DanceFestival",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FestivalComment",
                schema: "content",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    festival_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    written_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FestivalComment", x => new { x.entry_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_FestivalComment_DanceFestivalEntry_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "content",
                        principalTable: "DanceFestivalEntry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FestivalJuror",
                schema: "content",
                columns: table => new
                {
                    festival_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<int>(type: "int", nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FestivalJuror", x => new { x.festival_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_FestivalJuror_DanceFestival_festival_id",
                        column: x => x.festival_id,
                        principalSchema: "content",
                        principalTable: "DanceFestival",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FestivalScore",
                schema: "content",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    criterion = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    festival_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    value = table.Column<int>(type: "int", nullable: false),
                    scored_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FestivalScore", x => new { x.entry_id, x.user_id, x.criterion });
                    table.CheckConstraint("CK_FestivalScore_criterion", "[criterion] IN ('Synchronicity', 'Choreography', 'Execution', 'Difficulty')");
                    table.CheckConstraint("CK_FestivalScore_value", "[value] >= 0 AND [value] <= 10");
                    table.ForeignKey(
                        name: "FK_FestivalScore_DanceFestivalEntry_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "content",
                        principalTable: "DanceFestivalEntry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[,]
                {
                    { 54, "Content", "festival.judge", "Dansgardefestival: jureren in de app" },
                    { 55, "Content", "festival.head-jury", "Dansgardefestival: hoofdjury (uitslag voor de prijsuitreiking, prijsuitreiking afronden)" }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Role",
                columns: new[] { "id", "code", "description", "is_assignable_by_sync", "is_system", "name", "sort_order" },
                values: new object[,]
                {
                    { 206, "jury-dansgardefestival", "Jurylid van het Dansgardefestival: beoordeelt in de app alle dansen", false, false, "Jury dansgardefestival", 230 },
                    { 207, "hoofdjury-dansgardefestival", "Ziet de uitslag voor de prijsuitreiking en rondt die af (app)", false, false, "Hoofdjury dansgardefestival", 240 }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 54, 206 },
                    { 55, 207 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_FestivalComment_festival_id_user_id",
                schema: "content",
                table: "FestivalComment",
                columns: new[] { "festival_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_FestivalJuror_festival_id_number",
                schema: "content",
                table: "FestivalJuror",
                columns: new[] { "festival_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FestivalScore_festival_id_user_id",
                schema: "content",
                table: "FestivalScore",
                columns: new[] { "festival_id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FestivalCategorySubmission",
                schema: "content");

            migrationBuilder.DropTable(
                name: "FestivalComment",
                schema: "content");

            migrationBuilder.DropTable(
                name: "FestivalJuror",
                schema: "content");

            migrationBuilder.DropTable(
                name: "FestivalScore",
                schema: "content");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 54, 206 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 55, 207 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 206);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 207);

            migrationBuilder.DropColumn(
                name: "awards_completed_at",
                schema: "content",
                table: "DanceFestival");

            migrationBuilder.DropColumn(
                name: "awards_completed_by",
                schema: "content",
                table: "DanceFestival");

            migrationBuilder.DropColumn(
                name: "head_jury_published_at",
                schema: "content",
                table: "DanceFestival");
        }
    }
}
