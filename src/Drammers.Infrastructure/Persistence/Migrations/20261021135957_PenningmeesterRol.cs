using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PenningmeesterRol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 201,
                column: "sort_order",
                value: 190);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 202,
                column: "sort_order",
                value: 200);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 203,
                column: "sort_order",
                value: 210);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 204,
                column: "sort_order",
                value: 220);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 205,
                column: "sort_order",
                value: 230);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 206,
                column: "sort_order",
                value: 240);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 207,
                column: "sort_order",
                value: 250);

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Role",
                columns: new[] { "id", "code", "description", "is_assignable_by_sync", "is_system", "name", "sort_order" },
                values: new object[] { 102, "penningmeester", "Inkomsten en Mollie-afrekeningen, facturen, betalingen en contributie", false, false, "Penningmeester", 180 });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 1, 102 },
                    { 2, 102 },
                    { 9, 102 },
                    { 11, 102 },
                    { 13, 102 },
                    { 15, 102 },
                    { 28, 102 },
                    { 33, 102 },
                    { 48, 102 },
                    { 50, 102 },
                    { 56, 102 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 1, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 2, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 9, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 11, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 13, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 15, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 28, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 33, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 48, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 50, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 56, 102 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 102);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 201,
                column: "sort_order",
                value: 180);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 202,
                column: "sort_order",
                value: 190);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 203,
                column: "sort_order",
                value: 200);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 204,
                column: "sort_order",
                value: 210);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 205,
                column: "sort_order",
                value: 220);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 206,
                column: "sort_order",
                value: 230);

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 207,
                column: "sort_order",
                value: 240);
        }
    }
}
