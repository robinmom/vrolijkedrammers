using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HoofdklasseHalvePunten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FestivalScore_value",
                schema: "content",
                table: "FestivalScore");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DanceFestivalEntry_category",
                schema: "content",
                table: "DanceFestivalEntry");

            // De categorie "Senioren" heet voortaan "Hoofdklasse" (organisatie, 2026-11-02). De naam staat als tekst in de
            // database: in de categorie van een dans en vooraan de categoriesleutel van het programma en van wat juryleden
            // al hebben ingediend. De oude versies van een inschrijving (JSON) blijven staan zoals ze toen waren.
            migrationBuilder.Sql("UPDATE [content].[DanceFestivalEntry] SET [category] = 'Hoofdklasse' WHERE [category] = 'Senioren';");
            migrationBuilder.Sql("UPDATE [content].[DanceFestivalProgramItem] SET [category_key] = STUFF([category_key], 1, 8, 'Hoofdklasse') WHERE [category_key] LIKE 'Senioren|%';");
            migrationBuilder.Sql("UPDATE [content].[FestivalCategorySubmission] SET [category_key] = STUFF([category_key], 1, 8, 'Hoofdklasse') WHERE [category_key] LIKE 'Senioren|%';");

            // Halve punten (organisatie, 2026-11-02): hele punten passen zonder omzetting in decimal(3,1).
            migrationBuilder.AlterColumn<decimal>(
                name: "value",
                schema: "content",
                table: "FestivalScore",
                type: "decimal(3,1)",
                precision: 3,
                scale: 1,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FestivalScore_value",
                schema: "content",
                table: "FestivalScore",
                sql: "[value] >= 0 AND [value] <= 10 AND [value] % 0.5 = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DanceFestivalEntry_category",
                schema: "content",
                table: "DanceFestivalEntry",
                sql: "[category] IN ('Minioren', 'Junioren', 'Jeugd', 'Hoofdklasse')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FestivalScore_value",
                schema: "content",
                table: "FestivalScore");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DanceFestivalEntry_category",
                schema: "content",
                table: "DanceFestivalEntry");

            migrationBuilder.Sql("UPDATE [content].[DanceFestivalEntry] SET [category] = 'Senioren' WHERE [category] = 'Hoofdklasse';");
            migrationBuilder.Sql("UPDATE [content].[DanceFestivalProgramItem] SET [category_key] = STUFF([category_key], 1, 11, 'Senioren') WHERE [category_key] LIKE 'Hoofdklasse|%';");
            migrationBuilder.Sql("UPDATE [content].[FestivalCategorySubmission] SET [category_key] = STUFF([category_key], 1, 11, 'Senioren') WHERE [category_key] LIKE 'Hoofdklasse|%';");

            // Terug naar hele punten: halve punten worden daarbij afgekapt.
            migrationBuilder.AlterColumn<int>(
                name: "value",
                schema: "content",
                table: "FestivalScore",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(3,1)",
                oldPrecision: 3,
                oldScale: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FestivalScore_value",
                schema: "content",
                table: "FestivalScore",
                sql: "[value] >= 0 AND [value] <= 10");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DanceFestivalEntry_category",
                schema: "content",
                table: "DanceFestivalEntry",
                sql: "[category] IN ('Minioren', 'Junioren', 'Jeugd', 'Senioren')");
        }
    }
}
