using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FactuurMolliePogingen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdvertiserInvoiceMolliePayment",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mollie_payment_id = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    mollie_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    paid_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    duplicate_paid = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertiserInvoiceMolliePayment", x => x.id);
                    table.ForeignKey(
                        name: "FK_AdvertiserInvoiceMolliePayment_AdvertiserInvoice_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "membership",
                        principalTable: "AdvertiserInvoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertiserInvoiceMolliePayment_invoice_id",
                schema: "payments",
                table: "AdvertiserInvoiceMolliePayment",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_AdvertiserInvoiceMolliePayment_mollie_payment_id",
                schema: "payments",
                table: "AdvertiserInvoiceMolliePayment",
                column: "mollie_payment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvertiserInvoiceMolliePayment",
                schema: "payments");
        }
    }
}
