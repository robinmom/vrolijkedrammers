using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrinsAdjudanten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleInfo",
                schema: "config",
                columns: table => new
                {
                    role_code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleInfo", x => x.role_code);
                });

            // Een eerder zelf aangemaakte rol met dezelfde code blijft bestaan onder "<code>-eigen" (unieke index op code).
            migrationBuilder.Sql(
                "UPDATE [identity].[Role] SET [code] = [code] + '-eigen' WHERE [code] IN ('prins', 'adjudant') AND [id] NOT IN (203, 204);");

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Role",
                columns: new[] { "id", "code", "description", "is_assignable_by_sync", "is_system", "name", "sort_order" },
                values: new object[,]
                {
                    { 203, "prins", "De prins of prinses van het carnavalsjaar (maximaal één)", false, false, "Prins(es)", 200 },
                    { 204, "adjudant", "Adjudant van de prins(es) (maximaal twee)", false, false, "Adjudant", 210 }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 1, 203 },
                    { 9, 203 },
                    { 11, 203 },
                    { 13, 203 },
                    { 15, 203 },
                    { 28, 203 },
                    { 1, 204 },
                    { 9, 204 },
                    { 11, 204 },
                    { 13, 204 },
                    { 15, 204 },
                    { 28, 204 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleInfo",
                schema: "config");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 1, 203 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 9, 203 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 11, 203 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 13, 203 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 15, 203 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 28, 203 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 1, 204 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 9, 204 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 11, 204 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 13, 204 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 15, 204 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 28, 204 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 203);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 204);
        }
    }
}
