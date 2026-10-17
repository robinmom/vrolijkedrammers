using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CodeOpnieuwSturenLimiet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "verification_resends",
                schema: "parade",
                table: "ParadeRegistration",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "verification_resends",
                schema: "membership",
                table: "MembershipApplication",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "verification_resends",
                schema: "parade",
                table: "ParadeRegistration");

            migrationBuilder.DropColumn(
                name: "verification_resends",
                schema: "membership",
                table: "MembershipApplication");
        }
    }
}
