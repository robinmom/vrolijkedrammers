using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Contributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "contribution_exempt",
                schema: "membership",
                table: "Member",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "contribution_exempt_reason",
                schema: "membership",
                table: "Member",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "membership_kind",
                schema: "membership",
                table: "Member",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "payer_member_id",
                schema: "membership",
                table: "Member",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContributionRate",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    one_person = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    two_persons = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    one_person_senior = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    two_persons_senior = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    dansgarde = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionRate", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "membership",
                table: "ContributionRate",
                columns: new[] { "id", "dansgarde", "one_person", "one_person_senior", "two_persons", "two_persons_senior", "valid_from" },
                values: new object[] { 1, 85.00m, 32.50m, 22.00m, 57.50m, 44.00m, new DateOnly(2026, 1, 1) });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[] { 48, "Financieel", "contribution.manage", "Lidmaatschappen, tarieven, contributie en incasso beheren" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[] { 48, 11 });

            migrationBuilder.CreateIndex(
                name: "IX_Member_payer_member_id",
                schema: "membership",
                table: "Member",
                column: "payer_member_id",
                unique: true,
                filter: "[payer_member_id] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Member_membership_kind",
                schema: "membership",
                table: "Member",
                sql: "[membership_kind] IN ('OnePerson', 'TwoPersons', 'Partner', 'Dansgarde')");

            migrationBuilder.CreateIndex(
                name: "IX_ContributionRate_valid_from",
                schema: "membership",
                table: "ContributionRate",
                column: "valid_from",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Member_Member_payer_member_id",
                schema: "membership",
                table: "Member",
                column: "payer_member_id",
                principalSchema: "membership",
                principalTable: "Member",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Member_Member_payer_member_id",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropTable(
                name: "ContributionRate",
                schema: "membership");

            migrationBuilder.DropIndex(
                name: "IX_Member_payer_member_id",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Member_membership_kind",
                schema: "membership",
                table: "Member");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 48, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 48);

            migrationBuilder.DropColumn(
                name: "contribution_exempt",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "contribution_exempt_reason",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "membership_kind",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "payer_member_id",
                schema: "membership",
                table: "Member");
        }
    }
}
