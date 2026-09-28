using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParadeReviewAndGuests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 20, 1 });

            migrationBuilder.AddColumn<string>(
                name: "status_token_hash",
                schema: "parade",
                table: "ParadeRegistration",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "verification_attempts",
                schema: "parade",
                table: "ParadeRegistration",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "verification_code_hash",
                schema: "parade",
                table: "ParadeRegistration",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "verification_expires_at",
                schema: "parade",
                table: "ParadeRegistration",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "info_text",
                schema: "parade",
                table: "Parade",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "parade_group_name",
                schema: "membership",
                table: "Member",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ParadeBuildLocation",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    street = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    house_number = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: true),
                    addition = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    postal_code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    city = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    country = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeBuildLocation", x => x.id);
                    table.ForeignKey(
                        name: "FK_ParadeBuildLocation_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 2,
                columns: new[] { "description", "is_assignable_by_sync" },
                values: new object[] { "Mag groepen inschrijven voor de optocht (per gebruiker aangevinkt)", false });

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistration_status_token_hash",
                schema: "parade",
                table: "ParadeRegistration",
                column: "status_token_hash",
                unique: true,
                filter: "[status_token_hash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeBuildLocation_user_id",
                schema: "parade",
                table: "ParadeBuildLocation",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParadeBuildLocation",
                schema: "parade");

            migrationBuilder.DropIndex(
                name: "IX_ParadeRegistration_status_token_hash",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "status_token_hash",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "verification_attempts",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "verification_code_hash",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "verification_expires_at",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "info_text",
                schema: "parade",
                table: "Parade");

            migrationBuilder.DropColumn(
                name: "parade_group_name",
                schema: "membership",
                table: "Member");

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 2,
                columns: new[] { "description", "is_assignable_by_sync" },
                values: new object[] { "Beheert eigen optochtinschrijvingen", true });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[] { 20, 1 });
        }
    }
}
