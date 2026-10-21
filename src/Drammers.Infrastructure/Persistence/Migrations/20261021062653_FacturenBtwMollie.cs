using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FacturenBtwMollie : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mollie_payment_id",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mollie_status",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_method",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "service_fee_net",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "service_fee_vat",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "total_incl_vat",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_amount",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_rate",
                schema: "membership",
                table: "AdvertiserInvoice",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Bestaande facturen blijven ongewijzigd: totaal = het oude bedrag (geen btw met terugwerkende kracht),
            // betaalwijze uit de oude betaalwijze (machtiging/op rekening -> Mollie, contant -> contant) en de stand uit
            // wat al verstuurd/betaald was. Dit moet vóór de CHECK-constraints, anders faalt de lege standaardwaarde.
            // In EXEC gewikkeld zodat de nieuwe kolommen pas bij uitvoeren worden opgezocht (anders "Invalid column name"
            // omdat SQL Server de UPDATE in dezelfde batch compileert als de ADD COLUMN).
            migrationBuilder.Sql(
                """
                EXEC(N'
                UPDATE [membership].[AdvertiserInvoice] SET
                    [total_incl_vat] = [amount],
                    [payment_method] = CASE WHEN [payment] = ''Cash'' THEN ''Cash'' ELSE ''Mollie'' END,
                    [status] = CASE WHEN [paid_at] IS NOT NULL THEN ''Paid'' WHEN [sent_at] IS NOT NULL THEN ''Sent'' ELSE ''Concept'' END;
                ');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdvertiserInvoice_payment_method",
                schema: "membership",
                table: "AdvertiserInvoice",
                sql: "[payment_method] IN ('Cash', 'Mollie')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AdvertiserInvoice_status",
                schema: "membership",
                table: "AdvertiserInvoice",
                sql: "[status] IN ('Concept', 'Sent', 'Paid', 'Overdue', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AdvertiserInvoice_payment_method",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AdvertiserInvoice_status",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "mollie_payment_id",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "mollie_status",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "payment_method",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "service_fee_net",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "service_fee_vat",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "total_incl_vat",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "vat_amount",
                schema: "membership",
                table: "AdvertiserInvoice");

            migrationBuilder.DropColumn(
                name: "vat_rate",
                schema: "membership",
                table: "AdvertiserInvoice");
        }
    }
}
