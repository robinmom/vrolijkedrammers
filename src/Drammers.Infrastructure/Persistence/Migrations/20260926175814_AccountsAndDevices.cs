using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountsAndDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountRequest",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_number = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    mismatch_reason = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    rejection_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ip_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    decided_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountRequest", x => x.id);
                    table.CheckConstraint("CK_AccountRequest_status", "[status] IN ('Pending', 'Approved', 'Rejected', 'Duplicate')");
                    table.ForeignKey(
                        name: "FK_AccountRequest_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Device",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    installation_id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    platform = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    app_version = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    public_key = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    attestation_status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    trusted_scanner = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Device", x => x.id);
                    table.CheckConstraint("CK_Device_platform", "[platform] IN ('Ios', 'Android')");
                    table.CheckConstraint("CK_Device_status", "[status] IN ('Active', 'Revoked')");
                    table.ForeignKey(
                        name: "FK_Device_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrivacyRequest",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    requested_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    file_path = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivacyRequest", x => x.id);
                    table.CheckConstraint("CK_PrivacyRequest_status", "[status] IN ('Requested', 'Completed', 'Failed')");
                    table.CheckConstraint("CK_PrivacyRequest_type", "[type] IN ('Export', 'Erasure')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequest_member_id",
                schema: "identity",
                table: "AccountRequest",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_AccountRequest_status_requested_at",
                schema: "identity",
                table: "AccountRequest",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "IX_Device_user_id_installation_id",
                schema: "identity",
                table: "Device",
                columns: new[] { "user_id", "installation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivacyRequest_expires_at",
                schema: "membership",
                table: "PrivacyRequest",
                column: "expires_at",
                filter: "[file_path] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrivacyRequest_user_id",
                schema: "membership",
                table: "PrivacyRequest",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountRequest",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Device",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "PrivacyRequest",
                schema: "membership");
        }
    }
}
