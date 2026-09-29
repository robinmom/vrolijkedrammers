using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessByCarnivalDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                schema: "ticketing",
                table: "AccessScan",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<DateOnly>(
                name: "carnival_day",
                schema: "ticketing",
                table: "AccessScan",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessScan_carnival_day_member_id",
                schema: "ticketing",
                table: "AccessScan",
                columns: new[] { "carnival_day", "member_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessScan_event_or_day",
                schema: "ticketing",
                table: "AccessScan",
                sql: "([event_id] IS NULL AND [carnival_day] IS NOT NULL) OR ([event_id] IS NOT NULL AND [carnival_day] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessScan_carnival_day_member_id",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessScan_event_or_day",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.DropColumn(
                name: "carnival_day",
                schema: "ticketing",
                table: "AccessScan");

            migrationBuilder.AlterColumn<Guid>(
                name: "event_id",
                schema: "ticketing",
                table: "AccessScan",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
