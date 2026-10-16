using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FestivalProgramma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "program_order",
                schema: "content",
                table: "DanceFestivalEntry",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "drawn_at",
                schema: "content",
                table: "DanceFestival",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "program_start",
                schema: "content",
                table: "DanceFestival",
                type: "time",
                nullable: false,
                defaultValue: new TimeOnly(10, 30, 0));

            migrationBuilder.CreateTable(
                name: "DanceFestivalProgramItem",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    festival_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    category_key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    title = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    minutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanceFestivalProgramItem", x => x.id);
                    table.CheckConstraint("CK_DanceFestivalProgramItem_kind", "[kind] IN ('Category', 'Block')");
                    table.CheckConstraint("CK_DanceFestivalProgramItem_minutes", "[minutes] >= 0 AND [minutes] <= 240");
                    table.ForeignKey(
                        name: "FK_DanceFestivalProgramItem_DanceFestival_festival_id",
                        column: x => x.festival_id,
                        principalSchema: "content",
                        principalTable: "DanceFestival",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalProgramItem_festival_id_category_key",
                schema: "content",
                table: "DanceFestivalProgramItem",
                columns: new[] { "festival_id", "category_key" },
                unique: true,
                filter: "[category_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalProgramItem_festival_id_sort_order",
                schema: "content",
                table: "DanceFestivalProgramItem",
                columns: new[] { "festival_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DanceFestivalProgramItem",
                schema: "content");

            migrationBuilder.DropColumn(
                name: "program_order",
                schema: "content",
                table: "DanceFestivalEntry");

            migrationBuilder.DropColumn(
                name: "drawn_at",
                schema: "content",
                table: "DanceFestival");

            migrationBuilder.DropColumn(
                name: "program_start",
                schema: "content",
                table: "DanceFestival");
        }
    }
}
