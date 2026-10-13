using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Dansgardefestival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DanceFestival",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    festival_date = table.Column<DateOnly>(type: "date", nullable: false),
                    location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    page_enabled = table.Column<bool>(type: "bit", nullable: false),
                    registration_opens_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    registration_closes_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    payments_enabled = table.Column<bool>(type: "bit", nullable: false),
                    price_solo_duo_cents = table.Column<int>(type: "int", nullable: false),
                    price_group_one_dance_cents = table.Column<int>(type: "int", nullable: false),
                    price_group_two_dances_cents = table.Column<int>(type: "int", nullable: false),
                    notification_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    rules_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    privacy_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    last_registration_number = table.Column<int>(type: "int", nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanceFestival", x => x.id);
                    table.CheckConstraint("CK_DanceFestival_capacity", "[capacity] >= 0");
                    table.CheckConstraint("CK_DanceFestival_prices", "[price_solo_duo_cents] >= 0 AND [price_group_one_dance_cents] >= 0 AND [price_group_two_dances_cents] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "DanceFestivalRegistration",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    festival_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<int>(type: "int", nullable: false),
                    club_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    contact_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    address_line = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    postal_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    remarks = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    photo_consent = table.Column<bool>(type: "bit", nullable: false),
                    privacy_accepted_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    rules_accepted_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    changed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    access_token_protected = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    info_request = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanceFestivalRegistration", x => x.id);
                    table.ForeignKey(
                        name: "FK_DanceFestivalRegistration_DanceFestival_festival_id",
                        column: x => x.festival_id,
                        principalSchema: "content",
                        principalTable: "DanceFestival",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DanceFestivalEntry",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    dance_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    format = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    names = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    average_age = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    dancer_count = table.Column<int>(type: "int", nullable: false),
                    leader_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    show_style = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    category = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    holds_place = table.Column<bool>(type: "bit", nullable: false),
                    waitlist_position = table.Column<int>(type: "int", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    removed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanceFestivalEntry", x => x.id);
                    table.CheckConstraint("CK_DanceFestivalEntry_age", "[average_age] >= 0 AND [average_age] <= 99");
                    table.CheckConstraint("CK_DanceFestivalEntry_category", "[category] IN ('Minioren', 'Junioren', 'Jeugd', 'Senioren')");
                    table.CheckConstraint("CK_DanceFestivalEntry_dance_type", "[dance_type] IN ('Garde', 'Show')");
                    table.CheckConstraint("CK_DanceFestivalEntry_dancers", "[dancer_count] >= 1 AND [dancer_count] <= 200");
                    table.CheckConstraint("CK_DanceFestivalEntry_format", "[format] IN ('Solo', 'Duo', 'Group')");
                    table.CheckConstraint("CK_DanceFestivalEntry_show_style", "[show_style] IN ('Character', 'Modern')");
                    table.CheckConstraint("CK_DanceFestivalEntry_status", "[status] IN ('New', 'InfoRequested', 'Approved', 'Waitlisted', 'Rejected', 'Changed')");
                    table.ForeignKey(
                        name: "FK_DanceFestivalEntry_DanceFestivalRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "content",
                        principalTable: "DanceFestivalRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DanceFestivalPayment",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    mollie_payment_id = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    mollie_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    paid_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanceFestivalPayment", x => x.id);
                    table.CheckConstraint("CK_DanceFestivalPayment_amount", "[amount_cents] > 0");
                    table.CheckConstraint("CK_DanceFestivalPayment_status", "[status] IN ('Open', 'Paid', 'Canceled')");
                    table.ForeignKey(
                        name: "FK_DanceFestivalPayment_DanceFestivalRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "content",
                        principalTable: "DanceFestivalRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DanceFestivalRegistrationVersion",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanceFestivalRegistrationVersion", x => x.id);
                    table.ForeignKey(
                        name: "FK_DanceFestivalRegistrationVersion_DanceFestivalRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "content",
                        principalTable: "DanceFestivalRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[] { 52, "Content", "festival.manage", "Dansgardefestival: instellingen, inschrijvingen beoordelen, wachtlijst en betaallinks" });

            // Een in het portal aangemaakte rol met dezelfde code krijgt een andere code (zoals bij prins/adjudant).
            migrationBuilder.Sql(
                "UPDATE [identity].[Role] SET [code] = [code] + '-eigen' WHERE [code] = 'dansgardefestival' AND [id] <> 205;");

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Role",
                columns: new[] { "id", "code", "description", "is_assignable_by_sync", "is_system", "name", "sort_order" },
                values: new object[] { 205, "dansgardefestival", "Organisatie van het Dansgardefestival; ziet in het portal alleen het festival", false, false, "Dansgardefestival", 220 });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 52, 11 },
                    { 52, 205 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestival_active",
                schema: "content",
                table: "DanceFestival",
                column: "active",
                unique: true,
                filter: "[active] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalEntry_registration_id_sort_order",
                schema: "content",
                table: "DanceFestivalEntry",
                columns: new[] { "registration_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalPayment_mollie_payment_id",
                schema: "payments",
                table: "DanceFestivalPayment",
                column: "mollie_payment_id",
                filter: "[mollie_payment_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalPayment_registration_id",
                schema: "payments",
                table: "DanceFestivalPayment",
                column: "registration_id");

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalRegistration_festival_id_number",
                schema: "content",
                table: "DanceFestivalRegistration",
                columns: new[] { "festival_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalRegistrationVersion_registration_id_version",
                schema: "content",
                table: "DanceFestivalRegistrationVersion",
                columns: new[] { "registration_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DanceFestivalEntry",
                schema: "content");

            migrationBuilder.DropTable(
                name: "DanceFestivalPayment",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "DanceFestivalRegistrationVersion",
                schema: "content");

            migrationBuilder.DropTable(
                name: "DanceFestivalRegistration",
                schema: "content");

            migrationBuilder.DropTable(
                name: "DanceFestival",
                schema: "content");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 52, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 52, 205 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 205);
        }
    }
}
