using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Groups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Group",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Group", x => x.id);
                    table.CheckConstraint("CK_Group_type", "[type] IN ('DanceGuard', 'Committee', 'ParadeGroup', 'Other')");
                    table.ForeignKey(
                        name: "FK_Group_CarnivalYear_carnival_year_id",
                        column: x => x.carnival_year_id,
                        principalSchema: "content",
                        principalTable: "CarnivalYear",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroupMembership",
                schema: "membership",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    function = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembership", x => new { x.group_id, x.member_id });
                    table.CheckConstraint("CK_GroupMembership_function", "[function] IN ('Member', 'Lead')");
                    table.ForeignKey(
                        name: "FK_GroupMembership_Group_group_id",
                        column: x => x.group_id,
                        principalSchema: "membership",
                        principalTable: "Group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupMembership_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Group_carnival_year_id",
                schema: "membership",
                table: "Group",
                column: "carnival_year_id");

            migrationBuilder.CreateIndex(
                name: "IX_Group_name",
                schema: "membership",
                table: "Group",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembership_member_id",
                schema: "membership",
                table: "GroupMembership",
                column: "member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupMembership",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "Group",
                schema: "membership");
        }
    }
}
