using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAndRbac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "AccountProvisioning",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    source_id = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    step = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    eb_member_id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    member_number = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    entra_object_id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountProvisioning", x => x.id);
                    table.CheckConstraint("CK_AccountProvisioning_kind", "[kind] IN ('Member', 'Guardian', 'Administrator')");
                    table.CheckConstraint("CK_AccountProvisioning_source_type", "[source_type] IN ('MembershipApplication', 'AccountRequest', 'Guardian', 'Manual')");
                    table.CheckConstraint("CK_AccountProvisioning_step", "[step] IN ('Pending', 'EbCreated', 'MemberCreated', 'AccountCreated', 'WelcomeSent', 'Completed', 'Failed')");
                });

            migrationBuilder.CreateTable(
                name: "LoginHistory",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    subject_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    result = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ip_hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginHistory", x => x.id);
                    table.CheckConstraint("CK_LoginHistory_result", "[result] IN ('Success', 'Failed', 'Locked')");
                });

            migrationBuilder.CreateTable(
                name: "Permission",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permission", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Role",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    is_assignable_by_sync = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_object_id = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    account_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    last_login_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    permissions_version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.id);
                    table.CheckConstraint("CK_User_account_status", "[account_status] IN ('Active', 'Disabled', 'Blocked', 'Deleted')");
                });

            migrationBuilder.CreateTable(
                name: "RolePermission",
                schema: "identity",
                columns: table => new
                {
                    role_id = table.Column<int>(type: "int", nullable: false),
                    permission_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermission", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "FK_RolePermission_Permission_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "identity",
                        principalTable: "Permission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermission_Role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "Role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRole",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<int>(type: "int", nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: true),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: true),
                    assigned_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRole", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_UserRole_Role_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "Role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRole_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[,]
                {
                    { 1, "Leden", "member.read.own", "Eigen gegevens en lidmaatschap bekijken" },
                    { 2, "Leden", "member.read", "Leden bekijken en zoeken" },
                    { 3, "Leden", "member.update", "Lokale ledengegevens wijzigen" },
                    { 4, "Leden", "member.approve", "Lidmaatschapsaanvragen beoordelen" },
                    { 5, "Leden", "member.export", "Ledenlijsten exporteren" },
                    { 6, "Leden", "member.block", "Accounts en toegang blokkeren" },
                    { 7, "Leden", "member.privacy", "AVG-verzoeken afhandelen" },
                    { 8, "Leden", "guardian.read.own", "Gegevens en meldingen van eigen kinderen" },
                    { 9, "Content", "event.read", "Leden-events bekijken" },
                    { 10, "Content", "event.manage", "Agenda en programma beheren" },
                    { 11, "Content", "news.read", "Ledennieuws bekijken" },
                    { 12, "Content", "news.manage", "Nieuws beheren en publiceren" },
                    { 13, "Content", "photo.read", "Ledenfoto's bekijken" },
                    { 14, "Content", "photo.manage", "Albums en foto's beheren" },
                    { 15, "Meldingen", "notification.read.own", "Eigen inbox" },
                    { 16, "Meldingen", "notification.send", "Meldingen versturen naar elke doelgroep" },
                    { 17, "Meldingen", "notification.send.group", "Meldingen versturen naar eigen groep(en)" },
                    { 18, "Meldingen", "notification.send.urgent", "Categorie Dringend gebruiken" },
                    { 19, "Optocht", "parade.read", "Optochtinschrijvingen inzien" },
                    { 20, "Optocht", "parade.register", "Optochtinschrijving starten" },
                    { 21, "Optocht", "parade.update", "Eigen inschrijving wijzigen binnen het statusbeleid" },
                    { 22, "Optocht", "parade.manage", "Inschrijvingen beoordelen en wijzigen" },
                    { 23, "Optocht", "parade.manage-final", "Wijzigen na status Final" },
                    { 24, "Optocht", "parade.assign-start-number", "Startnummers en volgorde toekennen" },
                    { 25, "Optocht", "parade.import-arrival-times", "Aanrijtijden importeren en publiceren" },
                    { 26, "Optocht", "parade.export", "Optochtexports" },
                    { 27, "Optocht", "parade.config", "Optochten en categorieën configureren" },
                    { 28, "Toegang", "ticket.read.own", "Eigen tickets en QR" },
                    { 29, "Toegang", "ticket.read", "Tickets en scanlogs inzien" },
                    { 30, "Toegang", "ticket.scan", "Scanmodus gebruiken (alleen op trusted device)" },
                    { 31, "Toegang", "ticket.scan.details", "Extra details bij een scan" },
                    { 32, "Toegang", "ticket.manage", "Tickets en scanners beheren" },
                    { 33, "Financieel", "payment.read", "Betalingen en orders inzien" },
                    { 34, "Financieel", "payment.manage", "Refunds en handmatige correcties" },
                    { 35, "Beheer", "report.view", "Rapportages en dashboards" },
                    { 36, "Beheer", "import.run", "Sync en imports starten, conflicten afhandelen" },
                    { 37, "Beheer", "audit.read", "Auditlog inzien" },
                    { 38, "Beheer", "role.manage", "Rollen, permissions en toewijzingen beheren" },
                    { 39, "Beheer", "config.manage", "Carnavalsjaar, feature flags, appversie, bewaartermijnen" }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Role",
                columns: new[] { "id", "code", "description", "is_assignable_by_sync", "is_system", "name", "sort_order" },
                values: new object[,]
                {
                    { 1, "lid", "Lid van de vereniging (systeemrol)", true, true, "Carnavalist", 10 },
                    { 2, "groepsverantwoordelijke", "Beheert eigen optochtinschrijvingen", true, false, "Groepsverantwoordelijke", 20 },
                    { 3, "kaderlid", "Kader; vooral via doelgroepen", true, false, "Kaderlid", 30 },
                    { 4, "dansgarde-leiding", "Leiding van de dansgarde", true, false, "Dansgarde leiding", 40 },
                    { 5, "dansgarde-lid", "Lid van de dansgarde", true, false, "Dansgarde lid", 50 },
                    { 6, "ouder", "Ouder of verzorger van een minderjarig lid (systeemrol)", true, true, "Ouder/verzorger", 60 },
                    { 7, "raad-van-elf", "Raad van Elf", true, false, "Raad van Elf", 70 },
                    { 8, "scanner", "Mag scannen op een trusted device (eventueel tijdelijk)", false, false, "Scanner", 80 },
                    { 9, "optochtcommissie", "Organisatie van de optocht", false, false, "Optochtcommissie", 90 },
                    { 10, "redactie", "Nieuws, agenda en foto's", false, false, "Redactie", 100 },
                    { 11, "bestuur", "Bestuur van de vereniging (systeemrol)", false, true, "Bestuur", 110 },
                    { 12, "beheerder-it", "Technisch beheer, zonder inhoudelijke rechten op betalingen en goedkeuringen (systeemrol)", false, true, "Beheerder (IT)", 120 }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 1, 1 },
                    { 9, 1 },
                    { 11, 1 },
                    { 13, 1 },
                    { 15, 1 },
                    { 20, 1 },
                    { 28, 1 },
                    { 15, 2 },
                    { 20, 2 },
                    { 21, 2 },
                    { 1, 3 },
                    { 9, 3 },
                    { 11, 3 },
                    { 13, 3 },
                    { 15, 3 },
                    { 28, 3 },
                    { 1, 4 },
                    { 9, 4 },
                    { 11, 4 },
                    { 13, 4 },
                    { 15, 4 },
                    { 17, 4 },
                    { 28, 4 },
                    { 1, 5 },
                    { 9, 5 },
                    { 11, 5 },
                    { 13, 5 },
                    { 15, 5 },
                    { 28, 5 },
                    { 8, 6 },
                    { 15, 6 },
                    { 28, 6 },
                    { 1, 7 },
                    { 9, 7 },
                    { 11, 7 },
                    { 13, 7 },
                    { 15, 7 },
                    { 28, 7 },
                    { 35, 7 },
                    { 1, 8 },
                    { 9, 8 },
                    { 11, 8 },
                    { 13, 8 },
                    { 15, 8 },
                    { 28, 8 },
                    { 30, 8 },
                    { 1, 9 },
                    { 9, 9 },
                    { 11, 9 },
                    { 13, 9 },
                    { 15, 9 },
                    { 17, 9 },
                    { 19, 9 },
                    { 22, 9 },
                    { 24, 9 },
                    { 25, 9 },
                    { 26, 9 },
                    { 27, 9 },
                    { 28, 9 },
                    { 35, 9 },
                    { 1, 10 },
                    { 9, 10 },
                    { 10, 10 },
                    { 11, 10 },
                    { 12, 10 },
                    { 13, 10 },
                    { 14, 10 },
                    { 15, 10 },
                    { 16, 10 },
                    { 28, 10 },
                    { 1, 11 },
                    { 2, 11 },
                    { 3, 11 },
                    { 4, 11 },
                    { 5, 11 },
                    { 6, 11 },
                    { 7, 11 },
                    { 9, 11 },
                    { 10, 11 },
                    { 11, 11 },
                    { 12, 11 },
                    { 13, 11 },
                    { 14, 11 },
                    { 15, 11 },
                    { 16, 11 },
                    { 18, 11 },
                    { 19, 11 },
                    { 20, 11 },
                    { 22, 11 },
                    { 23, 11 },
                    { 24, 11 },
                    { 25, 11 },
                    { 26, 11 },
                    { 27, 11 },
                    { 28, 11 },
                    { 29, 11 },
                    { 30, 11 },
                    { 31, 11 },
                    { 32, 11 },
                    { 33, 11 },
                    { 35, 11 },
                    { 36, 11 },
                    { 37, 11 },
                    { 38, 11 },
                    { 39, 11 },
                    { 1, 12 },
                    { 2, 12 },
                    { 3, 12 },
                    { 6, 12 },
                    { 9, 12 },
                    { 11, 12 },
                    { 13, 12 },
                    { 15, 12 },
                    { 28, 12 },
                    { 36, 12 },
                    { 37, 12 },
                    { 38, 12 },
                    { 39, 12 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountProvisioning_source_type_source_id",
                schema: "identity",
                table: "AccountProvisioning",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginHistory_occurred_at",
                schema: "identity",
                table: "LoginHistory",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_LoginHistory_user_id_occurred_at",
                schema: "identity",
                table: "LoginHistory",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_Permission_code",
                schema: "identity",
                table: "Permission",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Role_code",
                schema: "identity",
                table: "Role",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermission_permission_id",
                schema: "identity",
                table: "RolePermission",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "IX_User_email",
                schema: "identity",
                table: "User",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "IX_User_external_object_id",
                schema: "identity",
                table: "User",
                column: "external_object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_member_id",
                schema: "identity",
                table: "User",
                column: "member_id",
                unique: true,
                filter: "[member_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserRole_role_id",
                schema: "identity",
                table: "UserRole",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountProvisioning",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "LoginHistory",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "RolePermission",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserRole",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Permission",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Role",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "User",
                schema: "identity");
        }
    }
}
