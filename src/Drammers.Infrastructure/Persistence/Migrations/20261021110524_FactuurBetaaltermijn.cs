using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FactuurBetaaltermijn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Bestaande facturen: uiterste betaaldatum = factuurdatum + 14 dagen (de oude vaste termijn). In EXEC zodat de
            // nieuwe kolom pas bij uitvoeren wordt opgezocht.
            migrationBuilder.Sql("EXEC(N'UPDATE [membership].[AdvertiserInvoice] SET [due_date] = DATEADD(day, 14, [invoice_date]);');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "due_date",
                schema: "membership",
                table: "AdvertiserInvoice");
        }
    }
}
