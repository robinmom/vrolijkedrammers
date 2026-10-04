using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvertiserInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdvertiserInvoice",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    advertiser_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    description = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    payment = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    company_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    address_line = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    postal_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    mandate_reference = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    iban_last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    paid_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertiserInvoice", x => x.id);
                    table.CheckConstraint("CK_AdvertiserInvoice_payment", "[payment] IN ('Mandate', 'Cash')");
                    table.ForeignKey(
                        name: "FK_AdvertiserInvoice_Advertiser_advertiser_id",
                        column: x => x.advertiser_id,
                        principalSchema: "membership",
                        principalTable: "Advertiser",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertiserInvoice_advertiser_id_year",
                schema: "membership",
                table: "AdvertiserInvoice",
                columns: new[] { "advertiser_id", "year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertiserInvoice_number",
                schema: "membership",
                table: "AdvertiserInvoice",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertiserInvoice_year_sequence",
                schema: "membership",
                table: "AdvertiserInvoice",
                columns: new[] { "year", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvertiserInvoice",
                schema: "membership");
        }
    }
}
