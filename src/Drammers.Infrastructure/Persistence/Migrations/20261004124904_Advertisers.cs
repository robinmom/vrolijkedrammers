using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Advertisers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Advertiser",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<int>(type: "int", nullable: false),
                    company_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    mobile = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    address_line = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    postal_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    website = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    page = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    payment = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    iban_protected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    iban_last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    mandate_reference = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    collector_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    imported_collector_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    added_via_app = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advertiser", x => x.id);
                    table.CheckConstraint("CK_Advertiser_kind", "[kind] IN ('Advertisement', 'FreeGift', 'Gift')");
                    table.CheckConstraint("CK_Advertiser_payment", "[payment] IN ('Mandate', 'Cash')");
                    table.ForeignKey(
                        name: "FK_Advertiser_Member_collector_member_id",
                        column: x => x.collector_member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AdvertiserYear",
                schema: "membership",
                columns: table => new
                {
                    advertiser_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    is_free = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status_changed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    status_changed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertiserYear", x => new { x.advertiser_id, x.year });
                    table.CheckConstraint("CK_AdvertiserYear_status", "[status] IN ('Open', 'Collected', 'Stopped')");
                    table.ForeignKey(
                        name: "FK_AdvertiserYear_Advertiser_advertiser_id",
                        column: x => x.advertiser_id,
                        principalSchema: "membership",
                        principalTable: "Advertiser",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[] { 50, "Financieel", "advertiser.manage", "Adverteerders beheren, het overzicht inlezen en de campagne volgen (incl. IBAN)" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[] { 50, 11 });

            migrationBuilder.CreateIndex(
                name: "IX_Advertiser_collector_member_id",
                schema: "membership",
                table: "Advertiser",
                column: "collector_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_Advertiser_number",
                schema: "membership",
                table: "Advertiser",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertiserYear_year_status",
                schema: "membership",
                table: "AdvertiserYear",
                columns: new[] { "year", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvertiserYear",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "Advertiser",
                schema: "membership");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 50, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 50);
        }
    }
}
