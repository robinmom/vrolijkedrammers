using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "config");

            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "content");

            migrationBuilder.EnsureSchema(
                name: "notification");

            migrationBuilder.CreateTable(
                name: "AppConfiguration",
                schema: "config",
                columns: table => new
                {
                    key = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppConfiguration", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    occurred_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    action = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    old_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ip_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correlation_id = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    hash_prev = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    hash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.id);
                    table.CheckConstraint("CK_AuditLog_actor_type", "[actor_type] IN ('User', 'System', 'Sync', 'Webhook')");
                });

            migrationBuilder.CreateTable(
                name: "CarnivalYear",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    carnival_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    carnival_end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarnivalYear", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlag",
                schema: "config",
                columns: table => new
                {
                    key = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    audience = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlag", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    locked_until = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "RetentionPolicy",
                schema: "config",
                columns: table => new
                {
                    data_type = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    retention_days = table.Column<int>(type: "int", nullable: false),
                    action = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionPolicy", x => x.data_type);
                    table.CheckConstraint("CK_RetentionPolicy_action", "[action] IN ('Delete', 'Anonymize', 'Aggregate')");
                });

            migrationBuilder.CreateTable(
                name: "ScheduledJob",
                schema: "config",
                columns: table => new
                {
                    name = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    last_started_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    last_completed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    last_succeeded_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    last_error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    last_instance = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledJob", x => x.name);
                });

            migrationBuilder.InsertData(
                schema: "config",
                table: "AppConfiguration",
                columns: new[] { "key", "created_at", "created_by", "updated_at", "updated_by", "value" },
                values: new object[,]
                {
                    { "maintenance_message", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "" },
                    { "maintenance_mode", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "false" },
                    { "min_app_version_android", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "1.0.0" },
                    { "min_app_version_ios", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "1.0.0" },
                    { "recommended_app_version", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "1.0.0" },
                    { "support_email", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "" }
                });

            migrationBuilder.InsertData(
                schema: "content",
                table: "CarnivalYear",
                columns: new[] { "id", "active", "carnival_end_date", "carnival_start_date", "end_date", "name", "start_date" },
                values: new object[] { 1, true, new DateOnly(2027, 2, 9), new DateOnly(2027, 2, 6), new DateOnly(2027, 2, 10), "2026/2027", new DateOnly(2026, 11, 11) });

            migrationBuilder.InsertData(
                schema: "config",
                table: "RetentionPolicy",
                columns: new[] { "data_type", "action", "created_at", "created_by", "retention_days", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { "account_request_rejected", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 92, null, null },
                    { "audit_log", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 730, null, null },
                    { "audit_log_financial_privacy", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 2557, null, null },
                    { "guardian_account_without_child", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 183, null, null },
                    { "login_history", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 365, null, null },
                    { "member_former", "Anonymize", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 730, null, null },
                    { "membership_application_rejected", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 183, null, null },
                    { "notification", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 365, null, null },
                    { "parade_document", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 365, null, null },
                    { "parade_registration", "Anonymize", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 1096, null, null },
                    { "push_token_inactive", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 183, null, null },
                    { "ticket_order_payment", "Delete", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 2557, null, null },
                    { "ticket_scan", "Aggregate", new DateTime(2026, 9, 25, 0, 0, 0, 0, DateTimeKind.Utc), null, 730, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_actor_user_id_occurred_at",
                schema: "audit",
                table: "AuditLog",
                columns: new[] { "actor_user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_entity_type_entity_id_occurred_at",
                schema: "audit",
                table: "AuditLog",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_CarnivalYear_active",
                schema: "content",
                table: "CarnivalYear",
                column: "active",
                unique: true,
                filter: "[active] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CarnivalYear_name",
                schema: "content",
                table: "CarnivalYear",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_created_at",
                schema: "notification",
                table: "Outbox",
                column: "created_at",
                filter: "[processed_at] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConfiguration",
                schema: "config");

            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "CarnivalYear",
                schema: "content");

            migrationBuilder.DropTable(
                name: "FeatureFlag",
                schema: "config");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "RetentionPolicy",
                schema: "config");

            migrationBuilder.DropTable(
                name: "ScheduledJob",
                schema: "config");
        }
    }
}
