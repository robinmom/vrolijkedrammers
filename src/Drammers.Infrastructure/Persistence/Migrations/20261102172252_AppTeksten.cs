using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppTeksten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppTextOverride",
                schema: "config",
                columns: table => new
                {
                    key = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    original = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTextOverride", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppTextOverride",
                schema: "config");
        }
    }
}
