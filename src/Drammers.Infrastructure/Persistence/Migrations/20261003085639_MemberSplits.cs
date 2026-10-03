using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemberSplits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "split_from_member_id",
                schema: "membership",
                table: "MembershipApplication",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "second_member_name",
                schema: "membership",
                table: "Member",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MembershipSplitInvitation",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    sent_to = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    sent_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    times_sent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipSplitInvitation", x => x.id);
                    table.ForeignKey(
                        name: "FK_MembershipSplitInvitation_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipSplitInvitation_member_id",
                schema: "membership",
                table: "MembershipSplitInvitation",
                column: "member_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipSplitInvitation_token_hash",
                schema: "membership",
                table: "MembershipSplitInvitation",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MembershipSplitInvitation",
                schema: "membership");

            migrationBuilder.DropColumn(
                name: "split_from_member_id",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "second_member_name",
                schema: "membership",
                table: "Member");
        }
    }
}
