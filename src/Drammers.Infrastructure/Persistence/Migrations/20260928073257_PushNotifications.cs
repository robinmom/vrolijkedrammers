using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "push_on_publish",
                schema: "content",
                table: "News",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Notification",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(65)", maxLength: 65, nullable: false),
                    body = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    category = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    deep_link = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    audience_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_type = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    canceled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    canceled_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    expanded = table.Column<bool>(type: "bit", nullable: false),
                    recipient_count = table.Column<int>(type: "int", nullable: false),
                    push_count = table.Column<int>(type: "int", nullable: false),
                    delivered_count = table.Column<int>(type: "int", nullable: false),
                    failed_count = table.Column<int>(type: "int", nullable: false),
                    read_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notification", x => x.id);
                    table.CheckConstraint("CK_Notification_category", "[category] IN ('Urgent', 'Program', 'News', 'Parade', 'DanceGuard', 'Kader', 'Tickets', 'Reminder', 'System')");
                    table.CheckConstraint("CK_Notification_status", "[status] IN ('Scheduled', 'Sending', 'Sent', 'PartiallyFailed', 'Failed', 'Canceled')");
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreference",
                schema: "notification",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreference", x => new { x.user_id, x.category });
                    table.CheckConstraint("CK_NotificationPreference_category", "[category] IN ('Urgent', 'Program', 'News', 'Parade', 'DanceGuard', 'Kader', 'Tickets', 'Reminder', 'System')");
                    table.ForeignKey(
                        name: "FK_NotificationPreference_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushDevice",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    anonymous_install_id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    platform = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    protected_token = table.Column<string>(type: "varchar(1000)", unicode: false, maxLength: 1000, nullable: false),
                    token_hash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false),
                    last_registered_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    invalidated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevice", x => x.id);
                    table.ForeignKey(
                        name: "FK_PushDevice_Device_device_id",
                        column: x => x.device_id,
                        principalSchema: "identity",
                        principalTable: "Device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PushDevice_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "NotificationRecipient",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    notification_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    push_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    on_behalf_of_member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    delivery_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    read_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRecipient", x => x.id);
                    table.CheckConstraint("CK_NotificationRecipient_delivery_status", "[delivery_status] IN ('Pending', 'Sent', 'Delivered', 'Failed', 'NoDevice', 'OptedOut')");
                    table.ForeignKey(
                        name: "FK_NotificationRecipient_Notification_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notification",
                        principalTable: "Notification",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDelivery",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    recipient_id = table.Column<long>(type: "bigint", nullable: false),
                    notification_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    push_device_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    ticket_id = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    error_code = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDelivery", x => x.id);
                    table.CheckConstraint("CK_NotificationDelivery_status", "[status] IN ('Pending', 'Sent', 'Delivered', 'Failed', 'NoDevice', 'OptedOut')");
                    table.ForeignKey(
                        name: "FK_NotificationDelivery_NotificationRecipient_recipient_id",
                        column: x => x.recipient_id,
                        principalSchema: "notification",
                        principalTable: "NotificationRecipient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notification_created_at",
                schema: "notification",
                table: "Notification",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_Notification_source_type_source_id",
                schema: "notification",
                table: "Notification",
                columns: new[] { "source_type", "source_id" },
                unique: true,
                filter: "[source_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDelivery_notification_id_status",
                schema: "notification",
                table: "NotificationDelivery",
                columns: new[] { "notification_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDelivery_push_device_id",
                schema: "notification",
                table: "NotificationDelivery",
                column: "push_device_id");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDelivery_recipient_id",
                schema: "notification",
                table: "NotificationDelivery",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecipient_notification_id_user_id",
                schema: "notification",
                table: "NotificationRecipient",
                columns: new[] { "notification_id", "user_id" },
                unique: true,
                filter: "[user_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecipient_user_id_notification_id",
                schema: "notification",
                table: "NotificationRecipient",
                columns: new[] { "user_id", "notification_id" },
                filter: "[user_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PushDevice_anonymous_install_id",
                schema: "notification",
                table: "PushDevice",
                column: "anonymous_install_id",
                unique: true,
                filter: "[anonymous_install_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PushDevice_device_id",
                schema: "notification",
                table: "PushDevice",
                column: "device_id",
                unique: true,
                filter: "[device_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PushDevice_token_hash",
                schema: "notification",
                table: "PushDevice",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDevice_user_id",
                schema: "notification",
                table: "PushDevice",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDelivery",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationPreference",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "PushDevice",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "NotificationRecipient",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "Notification",
                schema: "notification");

            migrationBuilder.DropColumn(
                name: "push_on_publish",
                schema: "content",
                table: "News");
        }
    }
}
