using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JeugdInschrijving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "youth_of_registration_id",
                schema: "parade",
                table: "ParadeRegistration",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistration_youth_of_registration_id",
                schema: "parade",
                table: "ParadeRegistration",
                column: "youth_of_registration_id",
                unique: true,
                filter: "[youth_of_registration_id] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_ParadeRegistration_ParadeRegistration_youth_of_registration_id",
                schema: "parade",
                table: "ParadeRegistration",
                column: "youth_of_registration_id",
                principalSchema: "parade",
                principalTable: "ParadeRegistration",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParadeRegistration_ParadeRegistration_youth_of_registration_id",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropIndex(
                name: "IX_ParadeRegistration_youth_of_registration_id",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "youth_of_registration_id",
                schema: "parade",
                table: "ParadeRegistration");
        }
    }
}
