using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParadeResultsPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "results_published_at",
                schema: "parade",
                table: "Parade",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "results_published_by",
                schema: "parade",
                table: "Parade",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "results_published_at",
                schema: "parade",
                table: "Parade");

            migrationBuilder.DropColumn(
                name: "results_published_by",
                schema: "parade",
                table: "Parade");
        }
    }
}
