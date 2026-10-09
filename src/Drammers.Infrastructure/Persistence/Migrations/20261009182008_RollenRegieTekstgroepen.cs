using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RollenRegieTekstgroepen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bestaat er al een zelf aangemaakte rol met deze code (portal), dan blijft die bestaan onder een andere code;
            // anders faalt de unieke index op code.
            migrationBuilder.Sql(
                "UPDATE [identity].[Role] SET [code] = [code] + '-eigen' WHERE [code] IN ('regie', 'tekstgroepen') AND [id] NOT IN (201, 202);");

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Role",
                columns: new[] { "id", "code", "description", "is_assignable_by_sync", "is_system", "name", "sort_order" },
                values: new object[,]
                {
                    { 201, "regie", "Regie; doelgroep voor activiteiten, nieuws en meldingen", false, false, "Regie", 180 },
                    { 202, "tekstgroepen", "Tekstgroepen; doelgroep voor activiteiten, nieuws en meldingen", false, false, "Tekstgroepen", 190 }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 1, 201 },
                    { 9, 201 },
                    { 11, 201 },
                    { 13, 201 },
                    { 15, 201 },
                    { 28, 201 },
                    { 1, 202 },
                    { 9, 202 },
                    { 11, 202 },
                    { 13, 202 },
                    { 15, 202 },
                    { 28, 202 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 1, 201 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 9, 201 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 11, 201 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 13, 201 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 15, 201 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 28, 201 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 1, 202 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 9, 202 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 11, 202 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 13, 202 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 15, 202 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 28, 202 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 201);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 202);
        }
    }
}
