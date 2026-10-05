using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvertiserPaymentInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AdvertiserInvoice_payment",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Advertiser_payment",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdvertiserInvoice_payment",
                schema: "membership",
                table: "AdvertiserInvoice",
                sql: "[payment] IN ('Mandate', 'Cash', 'Invoice')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Advertiser_payment",
                schema: "membership",
                table: "Advertiser",
                sql: "[payment] IN ('Mandate', 'Cash', 'Invoice')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AdvertiserInvoice_payment",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Advertiser_payment",
                schema: "membership",
                table: "Advertiser");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdvertiserInvoice_payment",
                schema: "membership",
                table: "AdvertiserInvoice",
                sql: "[payment] IN ('Mandate', 'Cash')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Advertiser_payment",
                schema: "membership",
                table: "Advertiser",
                sql: "[payment] IN ('Mandate', 'Cash')");
        }
    }
}
