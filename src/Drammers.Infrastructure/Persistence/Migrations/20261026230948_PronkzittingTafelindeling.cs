using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PronkzittingTafelindeling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PronkzittingSeat",
                schema: "ticketing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    party_key = table.Column<string>(type: "nvarchar(110)", maxLength: 110, nullable: false),
                    row_index = table.Column<byte>(type: "tinyint", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronkzittingSeat", x => x.id);
                    table.CheckConstraint("CK_PronkzittingSeat_row", "[row_index] BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_PronkzittingSeat_SaleProduct_product_id",
                        column: x => x.product_id,
                        principalSchema: "ticketing",
                        principalTable: "SaleProduct",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PronkzittingSeatingLayout",
                schema: "ticketing",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tables_per_row = table.Column<int>(type: "int", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronkzittingSeatingLayout", x => x.product_id);
                    table.CheckConstraint("CK_PronkzittingSeatingLayout_tables", "[tables_per_row] BETWEEN 1 AND 50");
                    table.ForeignKey(
                        name: "FK_PronkzittingSeatingLayout_SaleProduct_product_id",
                        column: x => x.product_id,
                        principalSchema: "ticketing",
                        principalTable: "SaleProduct",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PronkzittingSeat_product_id_party_key",
                schema: "ticketing",
                table: "PronkzittingSeat",
                columns: new[] { "product_id", "party_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PronkzittingSeat",
                schema: "ticketing");

            migrationBuilder.DropTable(
                name: "PronkzittingSeatingLayout",
                schema: "ticketing");
        }
    }
}
