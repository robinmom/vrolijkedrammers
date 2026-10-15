using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FestivalUitslagRapport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "report_sent_at",
                schema: "content",
                table: "DanceFestivalRegistration",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reports_sent_at",
                schema: "content",
                table: "DanceFestival",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "results_news_id",
                schema: "content",
                table: "DanceFestival",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "report_sent_at",
                schema: "content",
                table: "DanceFestivalRegistration");

            migrationBuilder.DropColumn(
                name: "reports_sent_at",
                schema: "content",
                table: "DanceFestival");

            migrationBuilder.DropColumn(
                name: "results_news_id",
                schema: "content",
                table: "DanceFestival");
        }
    }
}
