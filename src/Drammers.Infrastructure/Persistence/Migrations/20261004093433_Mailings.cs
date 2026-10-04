using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Mailings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Mailing",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    preheader = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    blocks = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    sent_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    recipient_count = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mailing", x => x.id);
                    table.CheckConstraint("CK_Mailing_kind", "[kind] IN ('Newsletter', 'Invitation')");
                    table.CheckConstraint("CK_Mailing_status", "[status] IN ('Draft', 'Sending', 'Sent')");
                });

            migrationBuilder.CreateTable(
                name: "MailingList",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    all_members = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingList", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "MailingUnsubscribe",
                schema: "notification",
                columns: table => new
                {
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    unsubscribed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingUnsubscribe", x => x.email);
                });

            migrationBuilder.CreateTable(
                name: "MailingRecipient",
                schema: "notification",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    mailing_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    first_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingRecipient", x => x.id);
                    table.CheckConstraint("CK_MailingRecipient_status", "[status] IN ('Pending', 'Sent', 'Failed')");
                    table.ForeignKey(
                        name: "FK_MailingRecipient_Mailing_mailing_id",
                        column: x => x.mailing_id,
                        principalSchema: "notification",
                        principalTable: "Mailing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailingListAddress",
                schema: "notification",
                columns: table => new
                {
                    list_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingListAddress", x => new { x.list_id, x.email });
                    table.ForeignKey(
                        name: "FK_MailingListAddress_MailingList_list_id",
                        column: x => x.list_id,
                        principalSchema: "notification",
                        principalTable: "MailingList",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailingListGroup",
                schema: "notification",
                columns: table => new
                {
                    list_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingListGroup", x => new { x.list_id, x.group_id });
                    table.ForeignKey(
                        name: "FK_MailingListGroup_MailingList_list_id",
                        column: x => x.list_id,
                        principalSchema: "notification",
                        principalTable: "MailingList",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailingListMember",
                schema: "notification",
                columns: table => new
                {
                    list_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingListMember", x => new { x.list_id, x.member_id });
                    table.ForeignKey(
                        name: "FK_MailingListMember_MailingList_list_id",
                        column: x => x.list_id,
                        principalSchema: "notification",
                        principalTable: "MailingList",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MailingTarget",
                schema: "notification",
                columns: table => new
                {
                    mailing_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    list_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailingTarget", x => new { x.mailing_id, x.list_id });
                    table.ForeignKey(
                        name: "FK_MailingTarget_MailingList_list_id",
                        column: x => x.list_id,
                        principalSchema: "notification",
                        principalTable: "MailingList",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MailingTarget_Mailing_mailing_id",
                        column: x => x.mailing_id,
                        principalSchema: "notification",
                        principalTable: "Mailing",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Standaardgroep voor de nieuwsbrief: alle actieve leden met een e-mailadres.
            migrationBuilder.InsertData(
                schema: "notification",
                table: "MailingList",
                columns: new[] { "id", "name", "description", "all_members", "created_at" },
                values: new object[] { new Guid("0193a000-0000-7000-8000-000000000001"), "Alle leden", "Alle actieve leden met een e-mailadres (nieuwsbrief).", true, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[] { 49, "Content", "mailing.manage", "Mailings opstellen en versturen (nieuwsbrief, uitnodigingen) en mailinggroepen beheren" });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[] { 49, 11 });

            migrationBuilder.CreateIndex(
                name: "IX_Mailing_created_at",
                schema: "notification",
                table: "Mailing",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_MailingListMember_member_id",
                schema: "notification",
                table: "MailingListMember",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_MailingRecipient_mailing_id_email",
                schema: "notification",
                table: "MailingRecipient",
                columns: new[] { "mailing_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailingRecipient_mailing_id_status",
                schema: "notification",
                table: "MailingRecipient",
                columns: new[] { "mailing_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_MailingTarget_list_id",
                schema: "notification",
                table: "MailingTarget",
                column: "list_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailingListAddress",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "MailingListGroup",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "MailingListMember",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "MailingRecipient",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "MailingTarget",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "MailingUnsubscribe",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "MailingList",
                schema: "notification");

            migrationBuilder.DropTable(
                name: "Mailing",
                schema: "notification");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 49, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 49);
        }
    }
}
