using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemberRequestsAndBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "account_holder",
                schema: "membership",
                table: "Member",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "iban_last4",
                schema: "membership",
                table: "Member",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "iban_protected",
                schema: "membership",
                table: "Member",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mandate_reference",
                schema: "membership",
                table: "Member",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "mandate_signed_on",
                schema: "membership",
                table: "Member",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CombinationBreakRequest",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payer_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    partner_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiated_by_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    payer_agreed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    partner_agreed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    partner_iban_protected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    partner_iban_last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    partner_account_holder = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    partner_mandate_consent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    decided_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombinationBreakRequest", x => x.id);
                    table.CheckConstraint("CK_CombinationBreakRequest_status", "[status] IN ('AwaitingAgreement', 'AwaitingApproval', 'Approved', 'Rejected', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_CombinationBreakRequest_Member_partner_member_id",
                        column: x => x.partner_member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_CombinationBreakRequest_Member_payer_member_id",
                        column: x => x.payer_member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "MemberChangeRequest",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    address_line = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    postal_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    city = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    mobile_phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    iban_protected = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    iban_last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    account_holder = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    mandate_consent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberChangeRequest", x => x.id);
                    table.CheckConstraint("CK_MemberChangeRequest_status", "[status] IN ('Pending', 'Approved', 'Rejected', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_MemberChangeRequest_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CombinationBreakRequest_partner_member_id",
                schema: "membership",
                table: "CombinationBreakRequest",
                column: "partner_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_CombinationBreakRequest_payer_member_id_status",
                schema: "membership",
                table: "CombinationBreakRequest",
                columns: new[] { "payer_member_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberChangeRequest_member_id_status",
                schema: "membership",
                table: "MemberChangeRequest",
                columns: new[] { "member_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CombinationBreakRequest",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "MemberChangeRequest",
                schema: "membership");

            migrationBuilder.DropColumn(
                name: "account_holder",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "iban_last4",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "iban_protected",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "mandate_reference",
                schema: "membership",
                table: "Member");

            migrationBuilder.DropColumn(
                name: "mandate_signed_on",
                schema: "membership",
                table: "Member");
        }
    }
}
