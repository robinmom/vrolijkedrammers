using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Het kader krijgt eigen categorieën (wens 18-11-2026): "Bestuur" en "Raad van Elf" waren vaste waarden in een
    /// kolom, nu rijen in <c>membership.BoardBody</c> die de vereniging zelf aanvult. De twee bestaande categorieën
    /// komen er met vaste id's in en de plekken worden eraan gekoppeld, dus er gaat niets verloren.
    /// </summary>
    public partial class KaderCategorieen : Migration
    {
        private const string Board = "0b1dd8a4-3f3b-4e53-9a5a-6b3f7c2e7a01";
        private const string Council = "0b1dd8a4-3f3b-4e53-9a5a-6b3f7c2e7a02";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoardBody",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    has_terms = table.Column<bool>(type: "bit", nullable: false),
                    position = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardBody", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoardBody_name",
                schema: "membership",
                table: "BoardBody",
                column: "name",
                unique: true);

            // De twee categorieën die er al waren. Het bestuur is verkiesbaar (periodes van drie jaar); de raad van elf
            // begint en stopt wanneer iemand dat zelf wil.
            migrationBuilder.Sql($"""
                INSERT INTO membership.BoardBody (id, name, has_terms, position, created_at)
                SELECT '{Board}', 'Bestuur', 1, 1, SYSUTCDATETIME()
                WHERE NOT EXISTS (SELECT 1 FROM membership.BoardBody WHERE id = '{Board}');
                INSERT INTO membership.BoardBody (id, name, has_terms, position, created_at)
                SELECT '{Council}', 'Raad van Elf', 0, 2, SYSUTCDATETIME()
                WHERE NOT EXISTS (SELECT 1 FROM membership.BoardBody WHERE id = '{Council}');
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "body_id",
                schema: "membership",
                table: "BoardSeat",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Via EXEC: in dezelfde batch kent SQL Server de net toegevoegde kolom nog niet.
            migrationBuilder.Sql($"""
                EXEC('UPDATE membership.BoardSeat SET body_id = CASE body WHEN ''Board'' THEN ''{Board}'' ELSE ''{Council}'' END');
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_BoardSeat_body",
                schema: "membership",
                table: "BoardSeat");

            migrationBuilder.DropIndex(
                name: "IX_BoardSeat_body_end_date",
                schema: "membership",
                table: "BoardSeat");

            migrationBuilder.DropColumn(
                name: "body",
                schema: "membership",
                table: "BoardSeat");

            migrationBuilder.CreateIndex(
                name: "IX_BoardSeat_body_id_end_date",
                schema: "membership",
                table: "BoardSeat",
                columns: new[] { "body_id", "end_date" });

            migrationBuilder.AddForeignKey(
                name: "FK_BoardSeat_BoardBody_body_id",
                schema: "membership",
                table: "BoardSeat",
                column: "body_id",
                principalSchema: "membership",
                principalTable: "BoardBody",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BoardSeat_BoardBody_body_id",
                schema: "membership",
                table: "BoardSeat");

            migrationBuilder.DropIndex(
                name: "IX_BoardSeat_body_id_end_date",
                schema: "membership",
                table: "BoardSeat");

            migrationBuilder.AddColumn<string>(
                name: "body",
                schema: "membership",
                table: "BoardSeat",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            // Terug naar de vaste waarden: een eigen categorie bestaat daar niet, die wordt raad van elf.
            migrationBuilder.Sql($"""
                EXEC('UPDATE membership.BoardSeat SET body = CASE body_id WHEN ''{Board}'' THEN ''Board'' ELSE ''CouncilOfEleven'' END');
                """);

            migrationBuilder.DropColumn(
                name: "body_id",
                schema: "membership",
                table: "BoardSeat");

            migrationBuilder.DropTable(
                name: "BoardBody",
                schema: "membership");

            migrationBuilder.CreateIndex(
                name: "IX_BoardSeat_body_end_date",
                schema: "membership",
                table: "BoardSeat",
                columns: new[] { "body", "end_date" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_BoardSeat_body",
                schema: "membership",
                table: "BoardSeat",
                sql: "[body] IN ('Board', 'CouncilOfEleven')");
        }
    }
}
