using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MuntenToestelKoppeling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "purchase_device_id",
                schema: "payments",
                table: "SaleOrder",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "bound_at",
                schema: "ticketing",
                table: "OrderTicket",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "bound_device_id",
                schema: "ticketing",
                table: "OrderTicket",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderTicket_bound_device_id",
                schema: "ticketing",
                table: "OrderTicket",
                column: "bound_device_id");

            // Bestaande munten blijven op het toestel waar ze nu werken: dat van het ledenticket van de houder. Munten
            // zonder toestel worden eenmalig gekoppeld aan het eerste toestel waarop het lid ze bekijkt.
            migrationBuilder.Sql("""
                EXEC('UPDATE ot SET ot.bound_device_id = t.bound_device_id, ot.bound_at = SYSUTCDATETIME()
                FROM ticketing.OrderTicket ot
                JOIN payments.SaleOrder o ON o.id = ot.order_id
                JOIN ticketing.SaleProduct p ON p.id = o.product_id
                JOIN ticketing.Ticket t ON t.member_id = ot.holder_member_id AND t.carnival_year_id = o.carnival_year_id
                WHERE p.kind = ''Tokens'' AND ot.bound_at IS NULL AND t.bound_device_id IS NOT NULL')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderTicket_bound_device_id",
                schema: "ticketing",
                table: "OrderTicket");

            migrationBuilder.DropColumn(
                name: "purchase_device_id",
                schema: "payments",
                table: "SaleOrder");

            migrationBuilder.DropColumn(
                name: "bound_at",
                schema: "ticketing",
                table: "OrderTicket");

            migrationBuilder.DropColumn(
                name: "bound_device_id",
                schema: "ticketing",
                table: "OrderTicket");
        }
    }
}
