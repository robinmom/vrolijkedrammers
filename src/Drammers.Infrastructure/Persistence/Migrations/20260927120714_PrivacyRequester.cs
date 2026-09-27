using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrivacyRequester : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "requested_by",
                schema: "membership",
                table: "PrivacyRequest",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subject_name",
                schema: "membership",
                table: "PrivacyRequest",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivacyRequest_requested_at",
                schema: "membership",
                table: "PrivacyRequest",
                column: "requested_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrivacyRequest_requested_at",
                schema: "membership",
                table: "PrivacyRequest");

            migrationBuilder.DropColumn(
                name: "requested_by",
                schema: "membership",
                table: "PrivacyRequest");

            migrationBuilder.DropColumn(
                name: "subject_name",
                schema: "membership",
                table: "PrivacyRequest");
        }
    }
}
