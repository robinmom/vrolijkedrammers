using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MuntenVerplaatsen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "moved_at",
                schema: "ticketing",
                table: "OrderTicket",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "moved_by_user_id",
                schema: "ticketing",
                table: "OrderTicket",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "moved_at",
                schema: "ticketing",
                table: "OrderTicket");

            migrationBuilder.DropColumn(
                name: "moved_by_user_id",
                schema: "ticketing",
                table: "OrderTicket");
        }
    }
}
