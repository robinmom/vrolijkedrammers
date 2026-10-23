using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Verzamelfacturen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectiveInvoice",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    source = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    number = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total = table.Column<decimal>(type: "decimal(11,2)", precision: 11, scale: 2, nullable: false),
                    line_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectiveInvoice", x => x.id);
                    table.CheckConstraint("CK_CollectiveInvoice_source", "[source] IN ('Advertisers', 'Festival', 'Contribution')");
                });

            migrationBuilder.CreateTable(
                name: "CollectiveInvoiceLine",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    transaction_key = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    party = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    reference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectiveInvoiceLine", x => x.id);
                    table.ForeignKey(
                        name: "FK_CollectiveInvoiceLine_CollectiveInvoice_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "payments",
                        principalTable: "CollectiveInvoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectiveInvoice_carnival_year_id_source_sequence",
                schema: "payments",
                table: "CollectiveInvoice",
                columns: new[] { "carnival_year_id", "source", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectiveInvoice_number",
                schema: "payments",
                table: "CollectiveInvoice",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectiveInvoiceLine_invoice_id",
                schema: "payments",
                table: "CollectiveInvoiceLine",
                column: "invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectiveInvoiceLine",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "CollectiveInvoice",
                schema: "payments");
        }
    }
}
