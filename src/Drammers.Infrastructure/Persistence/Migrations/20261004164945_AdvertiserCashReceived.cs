using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvertiserCashReceived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at",
                schema: "membership",
                table: "AdvertiserYear",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "paid_by",
                schema: "membership",
                table: "AdvertiserYear",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "paid_at",
                schema: "membership",
                table: "AdvertiserYear");

            migrationBuilder.DropColumn(
                name: "paid_by",
                schema: "membership",
                table: "AdvertiserYear");
        }
    }
}
