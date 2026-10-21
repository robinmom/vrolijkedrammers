using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FestivalBtwServicekosten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "invoice_number",
                schema: "payments",
                table: "DanceFestivalPayment",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "net_cents",
                schema: "payments",
                table: "DanceFestivalPayment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "service_fee_net_cents",
                schema: "payments",
                table: "DanceFestivalPayment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "service_fee_vat_cents",
                schema: "payments",
                table: "DanceFestivalPayment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "vat_cents",
                schema: "payments",
                table: "DanceFestivalPayment",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "service_fee_cents",
                schema: "content",
                table: "DanceFestival",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "service_fee_on",
                schema: "content",
                table: "DanceFestival",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_rate",
                schema: "content",
                table: "DanceFestival",
                type: "decimal(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Bestaande betaalverzoeken blijven ongewijzigd: het oude bedrag telt als netto (geen btw met terugwerkende
            // kracht). Het bestaande festival krijgt standaard 21% btw. In EXEC zodat de nieuwe kolom pas bij uitvoeren
            // wordt opgezocht (niet in dezelfde batch als de ADD COLUMN).
            migrationBuilder.Sql("EXEC(N'UPDATE [payments].[DanceFestivalPayment] SET [net_cents] = [amount_cents];');");
            migrationBuilder.Sql("EXEC(N'UPDATE [content].[DanceFestival] SET [vat_rate] = 0.21;');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invoice_number",
                schema: "payments",
                table: "DanceFestivalPayment");

            migrationBuilder.DropColumn(
                name: "net_cents",
                schema: "payments",
                table: "DanceFestivalPayment");

            migrationBuilder.DropColumn(
                name: "service_fee_net_cents",
                schema: "payments",
                table: "DanceFestivalPayment");

            migrationBuilder.DropColumn(
                name: "service_fee_vat_cents",
                schema: "payments",
                table: "DanceFestivalPayment");

            migrationBuilder.DropColumn(
                name: "vat_cents",
                schema: "payments",
                table: "DanceFestivalPayment");

            migrationBuilder.DropColumn(
                name: "service_fee_cents",
                schema: "content",
                table: "DanceFestival");

            migrationBuilder.DropColumn(
                name: "service_fee_on",
                schema: "content",
                table: "DanceFestival");

            migrationBuilder.DropColumn(
                name: "vat_rate",
                schema: "content",
                table: "DanceFestival");
        }
    }
}
