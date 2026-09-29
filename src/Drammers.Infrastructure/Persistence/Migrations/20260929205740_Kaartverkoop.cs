using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Kaartverkoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "SaleOrderSequence",
                schema: "payments",
                columns: table => new
                {
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    last_number = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleOrderSequence", x => x.carnival_year_id);
                    table.ForeignKey(
                        name: "FK_SaleOrderSequence_CarnivalYear_carnival_year_id",
                        column: x => x.carnival_year_id,
                        principalSchema: "content",
                        principalTable: "CarnivalYear",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleProduct",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    date = table.Column<DateOnly>(type: "date", nullable: true),
                    price_cents = table.Column<int>(type: "int", nullable: false),
                    capacity = table.Column<int>(type: "int", nullable: true),
                    max_per_order = table.Column<int>(type: "int", nullable: false),
                    sale_opens_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    sale_closes_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    on_sale = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleProduct", x => x.id);
                    table.CheckConstraint("CK_SaleProduct_capacity", "[capacity] IS NULL OR [capacity] >= 0");
                    table.CheckConstraint("CK_SaleProduct_kind", "[kind] IN ('Pronkzitting', 'DayTicket', 'EventTicket', 'Tokens')");
                    table.CheckConstraint("CK_SaleProduct_max_per_order", "[max_per_order] BETWEEN 1 AND 500");
                    table.CheckConstraint("CK_SaleProduct_price", "[price_cents] >= 0");
                    table.ForeignKey(
                        name: "FK_SaleProduct_CarnivalYear_carnival_year_id",
                        column: x => x.carnival_year_id,
                        principalSchema: "content",
                        principalTable: "CarnivalYear",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleProduct_Event_event_id",
                        column: x => x.event_id,
                        principalSchema: "content",
                        principalTable: "Event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SaleOrder",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    payment_method = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    group_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    member_quantity = table.Column<int>(type: "int", nullable: false),
                    paid_quantity = table.Column<int>(type: "int", nullable: false),
                    amount_cents = table.Column<int>(type: "int", nullable: false),
                    buyer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    buyer_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    buyer_phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    buyer_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    buyer_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    access_token_protected = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    mollie_payment_id = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    hold_until = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    paid_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    waitlist_entry_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    cancel_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleOrder", x => x.id);
                    table.CheckConstraint("CK_SaleOrder_amount", "[amount_cents] >= 0");
                    table.CheckConstraint("CK_SaleOrder_channel", "[channel] IN ('App', 'Web', 'Portal')");
                    table.CheckConstraint("CK_SaleOrder_payment_method", "[payment_method] IN ('Free', 'Mollie', 'Cash')");
                    table.CheckConstraint("CK_SaleOrder_quantity", "[member_quantity] >= 0 AND [paid_quantity] >= 0 AND [member_quantity] + [paid_quantity] > 0");
                    table.CheckConstraint("CK_SaleOrder_status", "[status] IN ('AwaitingPayment', 'Confirmed', 'Cancelled', 'Expired')");
                    table.ForeignKey(
                        name: "FK_SaleOrder_CarnivalYear_carnival_year_id",
                        column: x => x.carnival_year_id,
                        principalSchema: "content",
                        principalTable: "CarnivalYear",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleOrder_Member_buyer_member_id",
                        column: x => x.buyer_member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SaleOrder_SaleProduct_product_id",
                        column: x => x.product_id,
                        principalSchema: "ticketing",
                        principalTable: "SaleProduct",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleOrder_User_buyer_user_id",
                        column: x => x.buyer_user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "WaitlistEntry",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    channel = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    group_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    member_quantity = table.Column<int>(type: "int", nullable: false),
                    paid_quantity = table.Column<int>(type: "int", nullable: false),
                    buyer_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    buyer_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    buyer_phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    remark = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    buyer_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    buyer_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    invited_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntry", x => x.id);
                    table.CheckConstraint("CK_WaitlistEntry_channel", "[channel] IN ('App', 'Web', 'Portal')");
                    table.CheckConstraint("CK_WaitlistEntry_quantity", "[member_quantity] >= 0 AND [paid_quantity] >= 0 AND [member_quantity] + [paid_quantity] > 0");
                    table.CheckConstraint("CK_WaitlistEntry_status", "[status] IN ('Waiting', 'Invited', 'Granted', 'Expired', 'Withdrawn')");
                    table.ForeignKey(
                        name: "FK_WaitlistEntry_Member_buyer_member_id",
                        column: x => x.buyer_member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WaitlistEntry_SaleProduct_product_id",
                        column: x => x.product_id,
                        principalSchema: "ticketing",
                        principalTable: "SaleProduct",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WaitlistEntry_User_buyer_user_id",
                        column: x => x.buyer_user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrderTicket",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    public_ref = table.Column<byte[]>(type: "binary(16)", fixedLength: true, maxLength: 16, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    holder_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    shared_from_ticket_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    used_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTicket", x => x.id);
                    table.CheckConstraint("CK_OrderTicket_public_ref", "DATALENGTH([public_ref]) = 16");
                    table.CheckConstraint("CK_OrderTicket_quantity", "[quantity] > 0");
                    table.CheckConstraint("CK_OrderTicket_status", "[status] IN ('Active', 'Used', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_OrderTicket_SaleOrder_order_id",
                        column: x => x.order_id,
                        principalSchema: "payments",
                        principalTable: "SaleOrder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[,]
                {
                    { 41, "Financieel", "sale.manage", "Kaartverkoop beheren: producten, bestellingen, contant, betaallinks en wachtlijst" },
                    { 42, "Financieel", "sale.collect", "Kassa: munten-QR scannen en bestellingen uitgeven" }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Role",
                columns: new[] { "id", "code", "description", "is_assignable_by_sync", "is_system", "name", "sort_order" },
                values: new object[] { 13, "kassa", "Mag bij de kassa munten-QR's scannen en bestellingen uitgeven", false, false, "Kassa", 130 });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 41, 11 },
                    { 1, 13 },
                    { 9, 13 },
                    { 11, 13 },
                    { 13, 13 },
                    { 15, 13 },
                    { 28, 13 },
                    { 42, 13 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderTicket_holder_member_id",
                schema: "ticketing",
                table: "OrderTicket",
                column: "holder_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_OrderTicket_order_id",
                schema: "ticketing",
                table: "OrderTicket",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_OrderTicket_public_ref",
                schema: "ticketing",
                table: "OrderTicket",
                column: "public_ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrder_buyer_member_id",
                schema: "payments",
                table: "SaleOrder",
                column: "buyer_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrder_buyer_user_id",
                schema: "payments",
                table: "SaleOrder",
                column: "buyer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrder_carnival_year_id_group_name",
                schema: "payments",
                table: "SaleOrder",
                columns: new[] { "carnival_year_id", "group_name" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrder_mollie_payment_id",
                schema: "payments",
                table: "SaleOrder",
                column: "mollie_payment_id",
                filter: "[mollie_payment_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrder_number",
                schema: "payments",
                table: "SaleOrder",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrder_product_id_status",
                schema: "payments",
                table: "SaleOrder",
                columns: new[] { "product_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleProduct_carnival_year_id_sort_order",
                schema: "ticketing",
                table: "SaleProduct",
                columns: new[] { "carnival_year_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleProduct_event_id",
                schema: "ticketing",
                table: "SaleProduct",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntry_buyer_member_id",
                schema: "ticketing",
                table: "WaitlistEntry",
                column: "buyer_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntry_buyer_user_id",
                schema: "ticketing",
                table: "WaitlistEntry",
                column: "buyer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntry_product_id_status_created_at",
                schema: "ticketing",
                table: "WaitlistEntry",
                columns: new[] { "product_id", "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderTicket",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "SaleOrderSequence",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "WaitlistEntry",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "SaleOrder",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "SaleProduct",
                schema: "ticketing");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 41, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 1, 13 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 9, 13 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 11, 13 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 13, 13 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 15, 13 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 28, 13 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 42, 13 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 13);
        }
    }
}
