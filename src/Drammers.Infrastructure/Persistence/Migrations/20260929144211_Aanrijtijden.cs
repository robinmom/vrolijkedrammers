using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Aanrijtijden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "arrival_time",
                schema: "parade",
                table: "ParadeRegistration",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "arrival_location",
                schema: "parade",
                table: "Parade",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "arrival_times_published_at",
                schema: "parade",
                table: "Parade",
                type: "datetime2(3)",
                nullable: true);

            // Bestaande optochten: de meldplek van de website (aan te passen onder Optocht → Aanrijtijden).
            migrationBuilder.Sql("EXEC(N'UPDATE parade.Parade SET arrival_location = N''Rotonde Holthuizen'' WHERE arrival_location IS NULL')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "arrival_time",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "arrival_location",
                schema: "parade",
                table: "Parade");

            migrationBuilder.DropColumn(
                name: "arrival_times_published_at",
                schema: "parade",
                table: "Parade");
        }
    }
}
