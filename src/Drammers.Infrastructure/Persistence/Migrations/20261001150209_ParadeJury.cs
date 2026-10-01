using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParadeJury : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Parade_carnival_year_id",
                schema: "parade",
                table: "Parade");

            migrationBuilder.CreateTable(
                name: "ParadeJudgingCategory",
                schema: "parade",
                columns: table => new
                {
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category_id = table.Column<int>(type: "int", nullable: false),
                    judged = table.Column<bool>(type: "bit", nullable: false),
                    weight_originality = table.Column<int>(type: "int", nullable: false),
                    weight_carnivalesque = table.Column<int>(type: "int", nullable: false),
                    weight_quality = table.Column<int>(type: "int", nullable: false),
                    weight_overall = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeJudgingCategory", x => new { x.parade_id, x.category_id });
                    table.CheckConstraint("CK_ParadeJudgingCategory_weights", "[weight_originality] BETWEEN 0 AND 5 AND [weight_carnivalesque] BETWEEN 0 AND 5 AND [weight_quality] BETWEEN 0 AND 5 AND [weight_overall] BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_ParadeJudgingCategory_ParadeCategory_category_id",
                        column: x => x.category_id,
                        principalSchema: "parade",
                        principalTable: "ParadeCategory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ParadeJudgingCategory_Parade_parade_id",
                        column: x => x.parade_id,
                        principalSchema: "parade",
                        principalTable: "Parade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParadeJurorAssignment",
                schema: "parade",
                columns: table => new
                {
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category_id = table.Column<int>(type: "int", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeJurorAssignment", x => new { x.parade_id, x.user_id, x.category_id });
                    table.ForeignKey(
                        name: "FK_ParadeJurorAssignment_ParadeCategory_category_id",
                        column: x => x.category_id,
                        principalSchema: "parade",
                        principalTable: "ParadeCategory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ParadeJurorAssignment_Parade_parade_id",
                        column: x => x.parade_id,
                        principalSchema: "parade",
                        principalTable: "Parade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParadeJurorAssignment_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[,]
                {
                    { 44, "Optocht", "parade.judge", "Jureren in de app (eigen categorieën)" },
                    { 45, "Optocht", "jury.assign", "Juryleden aan categorieën toewijzen en beoordelingen buiten categorie goedkeuren" },
                    { 46, "Optocht", "jury.manage", "Juryleden uitnodigen, hoofdjury aanwijzen en de weging instellen" },
                    { 47, "Optocht", "parade.result", "Uitslag van de optocht inzien, exporteren en publiceren" }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Role",
                columns: new[] { "id", "code", "description", "is_assignable_by_sync", "is_system", "name", "sort_order" },
                values: new object[,]
                {
                    { 14, "jury", "Jurylid van de optocht: beoordeelt in de app de toegewezen categorieën", false, false, "Jury", 140 },
                    { 15, "hoofdjury", "Wijst juryleden aan categorieën toe en keurt beoordelingen buiten categorie goed (beheerportal)", false, false, "Hoofdjury", 150 },
                    { 16, "uitslagcommissie", "Ziet, exporteert en publiceert de uitslag van de optocht", false, false, "Uitslagcommissie", 160 }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 45, 11 },
                    { 46, 11 },
                    { 44, 14 },
                    { 45, 15 },
                    { 47, 16 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Parade_carnival_year_id",
                schema: "parade",
                table: "Parade",
                column: "carnival_year_id");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeJudgingCategory_category_id",
                schema: "parade",
                table: "ParadeJudgingCategory",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeJurorAssignment_category_id",
                schema: "parade",
                table: "ParadeJurorAssignment",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeJurorAssignment_parade_id_category_id",
                schema: "parade",
                table: "ParadeJurorAssignment",
                columns: new[] { "parade_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ParadeJurorAssignment_user_id",
                schema: "parade",
                table: "ParadeJurorAssignment",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParadeJudgingCategory",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "ParadeJurorAssignment",
                schema: "parade");

            migrationBuilder.DropIndex(
                name: "IX_Parade_carnival_year_id",
                schema: "parade",
                table: "Parade");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 45, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 46, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 44, 14 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 45, 15 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 47, 16 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 46);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 47);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 16);

            migrationBuilder.CreateIndex(
                name: "IX_Parade_carnival_year_id",
                schema: "parade",
                table: "Parade",
                column: "carnival_year_id",
                unique: true);
        }
    }
}
