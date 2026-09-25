using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MemberSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "membership");

            migrationBuilder.EnsureSchema(
                name: "import");

            migrationBuilder.CreateTable(
                name: "Member",
                schema: "membership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_number = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    eb_member_id = table.Column<int>(type: "int", nullable: true),
                    full_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    salutation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    gender = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: true),
                    address_line = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    postal_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    city = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    country = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    mobile_phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    join_year = table.Column<short>(type: "smallint", nullable: true),
                    eb_status_raw = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    member_category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    name_prefix = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    last_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    name_corrected_manually = table.Column<bool>(type: "bit", nullable: false),
                    membership_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    local_status_override = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    membership_valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    membership_valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    eb_hash = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    eb_last_seen_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    eb_missing_since = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    sync_state = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Member", x => x.id);
                    table.CheckConstraint("CK_Member_local_status_override", "[local_status_override] IN ('Active', 'Inactive', 'Suspended', 'Deceased')");
                    table.CheckConstraint("CK_Member_membership_status", "[membership_status] IN ('Active', 'Inactive', 'Suspended', 'Deceased')");
                    table.CheckConstraint("CK_Member_sync_state", "[sync_state] IN ('InSync', 'Missing', 'Conflict')");
                });

            migrationBuilder.CreateTable(
                name: "SyncJob",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    dry_run = table.Column<bool>(type: "bit", nullable: false),
                    trigger = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    requested_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    total_in_source = table.Column<int>(type: "int", nullable: false),
                    created = table.Column<int>(type: "int", nullable: false),
                    updated = table.Column<int>(type: "int", nullable: false),
                    unchanged = table.Column<int>(type: "int", nullable: false),
                    missing = table.Column<int>(type: "int", nullable: false),
                    deactivated = table.Column<int>(type: "int", nullable: false),
                    reactivated = table.Column<int>(type: "int", nullable: false),
                    warnings = table.Column<int>(type: "int", nullable: false),
                    errors = table.Column<int>(type: "int", nullable: false),
                    conflicts = table.Column<int>(type: "int", nullable: false),
                    error_message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJob", x => x.id);
                    table.CheckConstraint("CK_SyncJob_status", "[status] IN ('Queued', 'Running', 'Succeeded', 'SucceededWithWarnings', 'Conflict', 'Failed')");
                    table.CheckConstraint("CK_SyncJob_trigger", "[trigger] IN ('Scheduled', 'Manual')");
                });

            migrationBuilder.CreateTable(
                name: "SyncConflict",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sync_job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    member_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    resolved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    resolution_note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflict", x => x.id);
                    table.CheckConstraint("CK_SyncConflict_status", "[status] IN ('Open', 'Accepted', 'Ignored')");
                    table.CheckConstraint("CK_SyncConflict_type", "[type] IN ('DuplicateMemberNumber', 'MemberNumberChanged', 'EmailChangedForActiveAccount', 'MassDeletionGuard')");
                    table.ForeignKey(
                        name: "FK_SyncConflict_SyncJob_sync_job_id",
                        column: x => x.sync_job_id,
                        principalSchema: "import",
                        principalTable: "SyncJob",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncJobItem",
                schema: "import",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    sync_job_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    changed_fields = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: true),
                    message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobItem", x => x.id);
                    table.CheckConstraint("CK_SyncJobItem_action", "[action] IN ('Created', 'Updated', 'Unchanged', 'Missing', 'Deactivated', 'Reactivated', 'Warning', 'Error', 'Conflict')");
                    table.ForeignKey(
                        name: "FK_SyncJobItem_SyncJob_sync_job_id",
                        column: x => x.sync_job_id,
                        principalSchema: "import",
                        principalTable: "SyncJob",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[] { 40, "Leden", "member.purge", "Alle leden uit de test-/acceptatieomgeving verwijderen (niet in productie)" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 40, 11 },
                    { 40, 12 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Member_eb_member_id",
                schema: "membership",
                table: "Member",
                column: "eb_member_id",
                unique: true,
                filter: "[eb_member_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Member_email",
                schema: "membership",
                table: "Member",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "IX_Member_member_number",
                schema: "membership",
                table: "Member",
                column: "member_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Member_membership_status_last_name",
                schema: "membership",
                table: "Member",
                columns: new[] { "membership_status", "last_name" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflict_status",
                schema: "import",
                table: "SyncConflict",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflict_sync_job_id",
                schema: "import",
                table: "SyncConflict",
                column: "sync_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJob_requested_at",
                schema: "import",
                table: "SyncJob",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJob_status",
                schema: "import",
                table: "SyncJob",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobItem_sync_job_id_action",
                schema: "import",
                table: "SyncJobItem",
                columns: new[] { "sync_job_id", "action" });

            migrationBuilder.AddForeignKey(
                name: "FK_User_Member_member_id",
                schema: "identity",
                table: "User",
                column: "member_id",
                principalSchema: "membership",
                principalTable: "Member",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_User_Member_member_id",
                schema: "identity",
                table: "User");

            migrationBuilder.DropTable(
                name: "Member",
                schema: "membership");

            migrationBuilder.DropTable(
                name: "SyncConflict",
                schema: "import");

            migrationBuilder.DropTable(
                name: "SyncJobItem",
                schema: "import");

            migrationBuilder.DropTable(
                name: "SyncJob",
                schema: "import");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 40, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 40, 12 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 40);
        }
    }
}
