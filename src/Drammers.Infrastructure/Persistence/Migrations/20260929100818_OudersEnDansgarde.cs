using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OudersEnDansgarde : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "membership_type",
                schema: "membership",
                table: "MembershipApplication",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "Individual");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "membership",
                table: "GuardianRelation",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "relationship",
                schema: "membership",
                table: "GuardianRelation",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "Parent");

            migrationBuilder.AddColumn<string>(
                name: "login_email",
                schema: "identity",
                table: "AccountProvisioning",
                type: "nvarchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GuardianLinkRequest",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    child_first_name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    child_last_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    relationship = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    decided_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianLinkRequest", x => x.id);
                    table.CheckConstraint("CK_GuardianLinkRequest_relationship", "[relationship] IN ('Parent', 'Caregiver')");
                    table.CheckConstraint("CK_GuardianLinkRequest_status", "[status] IN ('Pending', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_GuardianLinkRequest_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GuardianLinkRequest_User_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GuardianSuggestionDismissal",
                schema: "membership",
                columns: table => new
                {
                    child_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dismissed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    dismissed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianSuggestionDismissal", x => new { x.child_member_id, x.parent_member_id });
                    table.ForeignKey(
                        name: "FK_GuardianSuggestionDismissal_Member_child_member_id",
                        column: x => x.child_member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuardianSuggestionDismissal_Member_parent_member_id",
                        column: x => x.parent_member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MembershipApplication_membership_type",
                schema: "membership",
                table: "MembershipApplication",
                sql: "[membership_type] IN ('Individual', 'Dansgarde')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GuardianRelation_relationship",
                schema: "membership",
                table: "GuardianRelation",
                sql: "[relationship] IN ('Parent', 'Caregiver')");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLinkRequest_member_id",
                schema: "membership",
                table: "GuardianLinkRequest",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLinkRequest_requested_by_user_id",
                schema: "membership",
                table: "GuardianLinkRequest",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianLinkRequest_status_created_at",
                schema: "membership",
                table: "GuardianLinkRequest",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianSuggestionDismissal_parent_member_id",
                schema: "membership",
                table: "GuardianSuggestionDismissal",
                column: "parent_member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuardianLinkRequest",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "GuardianSuggestionDismissal",
                schema: "membership");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MembershipApplication_membership_type",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GuardianRelation_relationship",
                schema: "membership",
                table: "GuardianRelation");

            migrationBuilder.DropColumn(
                name: "membership_type",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "membership",
                table: "GuardianRelation");

            migrationBuilder.DropColumn(
                name: "relationship",
                schema: "membership",
                table: "GuardianRelation");

            migrationBuilder.DropColumn(
                name: "login_email",
                schema: "identity",
                table: "AccountProvisioning");
        }
    }
}
