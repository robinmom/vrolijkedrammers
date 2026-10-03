using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SepaCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectionRun",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    collection_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    message_id = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    line_count = table.Column<int>(type: "int", nullable: false),
                    total = table.Column<decimal>(type: "decimal(11,2)", precision: 11, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    exported_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRun", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "CollectionRunLine",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_number = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    debtor_name = table.Column<string>(type: "nvarchar(70)", maxLength: 70, nullable: false),
                    amount = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    mandate_reference = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    mandate_signed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    sequence_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    iban_last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    iban_protected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    end_to_end_id = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                    description = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRunLine", x => x.id);
                    table.CheckConstraint("CK_CollectionRunLine_sequence_type", "[sequence_type] IN ('Frst', 'Rcur')");
                    table.ForeignKey(
                        name: "FK_CollectionRunLine_CollectionRun_run_id",
                        column: x => x.run_id,
                        principalSchema: "membership",
                        principalTable: "CollectionRun",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRun_message_id",
                schema: "membership",
                table: "CollectionRun",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRunLine_mandate_reference",
                schema: "membership",
                table: "CollectionRunLine",
                column: "mandate_reference");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRunLine_run_id",
                schema: "membership",
                table: "CollectionRunLine",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionRunLine",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "CollectionRun",
                schema: "membership");
        }
    }
}
