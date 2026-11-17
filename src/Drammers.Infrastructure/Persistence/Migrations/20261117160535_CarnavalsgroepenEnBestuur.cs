using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CarnavalsgroepenEnBestuur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "carnival_group_id",
                schema: "parade",
                table: "ParadeRegistration",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BoardSeat",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    body = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    function = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    terms = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardSeat", x => x.id);
                    table.CheckConstraint("CK_BoardSeat_body", "[body] IN ('Board', 'CouncilOfEleven')");
                    table.CheckConstraint("CK_BoardSeat_period", "[end_date] IS NULL OR [end_date] >= [start_date]");
                    table.ForeignKey(
                        name: "FK_BoardSeat_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CarnivalGroup",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    match_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    contact_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    contact_phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    contact_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    participating_since = table.Column<int>(type: "int", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarnivalGroup", x => x.id);
                    table.CheckConstraint("CK_CarnivalGroup_since", "[participating_since] IS NULL OR [participating_since] BETWEEN 1900 AND 2200");
                });

            migrationBuilder.CreateTable(
                name: "CarnivalGroupSeason",
                schema: "parade",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    float_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    category_id = table.Column<int>(type: "int", nullable: true),
                    place = table.Column<int>(type: "int", nullable: true),
                    place_from_results_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarnivalGroupSeason", x => new { x.group_id, x.year });
                    table.CheckConstraint("CK_CarnivalGroupSeason_place", "[place] IS NULL OR [place] > 0");
                    table.CheckConstraint("CK_CarnivalGroupSeason_year", "[year] BETWEEN 1900 AND 2200");
                    table.ForeignKey(
                        name: "FK_CarnivalGroupSeason_CarnivalGroup_group_id",
                        column: x => x.group_id,
                        principalSchema: "parade",
                        principalTable: "CarnivalGroup",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CarnivalGroupSeason_ParadeCategory_category_id",
                        column: x => x.category_id,
                        principalSchema: "parade",
                        principalTable: "ParadeCategory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistration_carnival_group_id",
                schema: "parade",
                table: "ParadeRegistration",
                column: "carnival_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_BoardSeat_body_end_date",
                schema: "membership",
                table: "BoardSeat",
                columns: new[] { "body", "end_date" });

            migrationBuilder.CreateIndex(
                name: "IX_BoardSeat_member_id",
                schema: "membership",
                table: "BoardSeat",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_CarnivalGroup_match_name",
                schema: "parade",
                table: "CarnivalGroup",
                column: "match_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarnivalGroupSeason_category_id",
                schema: "parade",
                table: "CarnivalGroupSeason",
                column: "category_id");

            migrationBuilder.AddForeignKey(
                name: "FK_ParadeRegistration_CarnivalGroup_carnival_group_id",
                schema: "parade",
                table: "ParadeRegistration",
                column: "carnival_group_id",
                principalSchema: "parade",
                principalTable: "CarnivalGroup",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParadeRegistration_CarnivalGroup_carnival_group_id",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropTable(
                name: "BoardSeat",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "CarnivalGroupSeason",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "CarnivalGroup",
                schema: "parade");

            migrationBuilder.DropIndex(
                name: "IX_ParadeRegistration_carnival_group_id",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "carnival_group_id",
                schema: "parade",
                table: "ParadeRegistration");
        }
    }
}
