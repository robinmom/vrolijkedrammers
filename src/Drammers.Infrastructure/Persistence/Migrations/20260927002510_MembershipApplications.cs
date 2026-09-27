using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MembershipApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuardianRelation",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    guardian_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    guardian_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    guardian_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    verified_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuardianRelation", x => x.id);
                    table.ForeignKey(
                        name: "FK_GuardianRelation_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GuardianRelation_User_guardian_user_id",
                        column: x => x.guardian_user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MembershipApplication",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    source = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name_prefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    last_name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    gender = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: true),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    address_line = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    postal_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    city = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    guardian_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    guardian_email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    guardian_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    iban = table.Column<string>(type: "varchar(34)", unicode: false, maxLength: 34, nullable: true),
                    account_holder = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    mandate_consent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    mandate_reference = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: false),
                    consent_privacy_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    consent_photo = table.Column<bool>(type: "bit", nullable: false),
                    verification_code_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    verification_expires_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    verification_attempts = table.Column<int>(type: "int", nullable: false),
                    email_verified_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    handled_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    handled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    decision_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    internal_notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    resulting_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    provisioning_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ip_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipApplication", x => x.id);
                    table.CheckConstraint("CK_MembershipApplication_source", "[source] IN ('App', 'Website', 'Portal')");
                    table.CheckConstraint("CK_MembershipApplication_status", "[status] IN ('Draft', 'Submitted', 'InReview', 'Approved', 'Rejected', 'Provisioning', 'ProvisioningFailed', 'Activated', 'Withdrawn')");
                    table.ForeignKey(
                        name: "FK_MembershipApplication_Member_resulting_member_id",
                        column: x => x.resulting_member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuardianRelation_guardian_user_id",
                schema: "membership",
                table: "GuardianRelation",
                column: "guardian_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_GuardianRelation_member_id_guardian_user_id",
                schema: "membership",
                table: "GuardianRelation",
                columns: new[] { "member_id", "guardian_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipApplication_mandate_reference",
                schema: "membership",
                table: "MembershipApplication",
                column: "mandate_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipApplication_resulting_member_id",
                schema: "membership",
                table: "MembershipApplication",
                column: "resulting_member_id");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipApplication_status_submitted_at",
                schema: "membership",
                table: "MembershipApplication",
                columns: new[] { "status", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuardianRelation",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "MembershipApplication",
                schema: "membership");
        }
    }
}
