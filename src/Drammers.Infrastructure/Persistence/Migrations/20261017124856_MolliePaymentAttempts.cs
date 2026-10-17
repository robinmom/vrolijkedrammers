using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MolliePaymentAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DanceFestivalMolliePayment",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mollie_payment_id = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    mollie_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    paid_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    duplicate_paid = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DanceFestivalMolliePayment", x => x.id);
                    table.ForeignKey(
                        name: "FK_DanceFestivalMolliePayment_DanceFestivalPayment_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "DanceFestivalPayment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleOrderPayment",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mollie_payment_id = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    amount_cents = table.Column<int>(type: "int", nullable: false),
                    mollie_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    paid_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    refund_reason = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleOrderPayment", x => x.id);
                    table.CheckConstraint("CK_SaleOrderPayment_amount", "[amount_cents] >= 0");
                    table.CheckConstraint("CK_SaleOrderPayment_refund_reason", "[refund_reason] IN ('DuplicatePayment', 'NoPlace')");
                    table.ForeignKey(
                        name: "FK_SaleOrderPayment_SaleOrder_order_id",
                        column: x => x.order_id,
                        principalSchema: "payments",
                        principalTable: "SaleOrder",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalMolliePayment_mollie_payment_id",
                schema: "payments",
                table: "DanceFestivalMolliePayment",
                column: "mollie_payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DanceFestivalMolliePayment_payment_id",
                schema: "payments",
                table: "DanceFestivalMolliePayment",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrderPayment_mollie_payment_id",
                schema: "payments",
                table: "SaleOrderPayment",
                column: "mollie_payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrderPayment_order_id",
                schema: "payments",
                table: "SaleOrderPayment",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_SaleOrderPayment_refund_reason",
                schema: "payments",
                table: "SaleOrderPayment",
                column: "refund_reason",
                filter: "[refund_reason] IS NOT NULL");

            // De laatste Mollie-betaling van bestaande bestellingen en betaalverzoeken overnemen, zodat de webhook en het
            // opruimen ze blijven herkennen. Al verwerkte betalingen krijgen paid_at (niet nog eens verwerken).
            migrationBuilder.Sql(
                """
                INSERT INTO [payments].[SaleOrderPayment] ([id], [order_id], [mollie_payment_id], [amount_cents], [mollie_status], [created_at], [paid_at])
                SELECT NEWID(), [id], [mollie_payment_id], [amount_cents],
                       CASE WHEN [status] = 'Confirmed' AND [payment_method] = 'Mollie' THEN 'paid' END,
                       [created_at],
                       CASE WHEN [status] = 'Confirmed' AND [payment_method] = 'Mollie' THEN COALESCE([paid_at], [created_at]) END
                  FROM [payments].[SaleOrder]
                 WHERE [mollie_payment_id] IS NOT NULL;
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO [payments].[DanceFestivalMolliePayment] ([id], [payment_id], [mollie_payment_id], [mollie_status], [created_at], [paid_at], [duplicate_paid])
                SELECT NEWID(), [id], [mollie_payment_id], [mollie_status], [created_at],
                       CASE WHEN [status] = 'Paid' THEN COALESCE([paid_at], [created_at]) END, 0
                  FROM [payments].[DanceFestivalPayment]
                 WHERE [mollie_payment_id] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DanceFestivalMolliePayment",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "SaleOrderPayment",
                schema: "payments");
        }
    }
}
