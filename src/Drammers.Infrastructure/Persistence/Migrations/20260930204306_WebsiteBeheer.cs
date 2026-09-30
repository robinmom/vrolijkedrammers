using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebsiteBeheer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "show_on_website",
                schema: "content",
                table: "News",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "slug",
                schema: "content",
                table: "News",
                type: "varchar(120)",
                unicode: false,
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website_body",
                schema: "content",
                table: "News",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Award",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    recipient = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    photo_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    slug = table.Column<string>(type: "varchar(120)", unicode: false, maxLength: 120, nullable: false),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Award", x => x.id);
                    table.CheckConstraint("CK_Award_type", "[type] IN ('Drammertje', 'VerdienstelijkeDidammer', 'EikenloofVanBoschslag')");
                });

            migrationBuilder.CreateTable(
                name: "Committee",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "varchar(120)", unicode: false, maxLength: 120, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Committee", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Prince",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    year = table.Column<int>(type: "int", nullable: false),
                    prince_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    motto = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    photo_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prince", x => x.id);
                    table.CheckConstraint("CK_Prince_kind", "[kind] IN ('Prince', 'YouthPrince')");
                });

            migrationBuilder.CreateTable(
                name: "WebsitePage",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slug = table.Column<string>(type: "varchar(120)", unicode: false, maxLength: 120, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    intro = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    image_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsitePage", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "WebsiteSettings",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    hero_eyebrow = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    hero_title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    hero_subtitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    hero_primary_label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    hero_primary_link = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    hero_secondary_label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    hero_secondary_link = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    hero_image_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    facebook_page_url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    instagram_url = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    show_youth_princes = table.Column<bool>(type: "bit", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteSettings", x => x.id);
                    table.CheckConstraint("CK_WebsiteSettings_hero_primary_link", "[hero_primary_link] IN ('Agenda', 'News', 'Photos', 'Parade', 'ParadeRegistration', 'Membership', 'Tickets', 'App', 'Contact')");
                    table.CheckConstraint("CK_WebsiteSettings_hero_secondary_link", "[hero_secondary_link] IN ('Agenda', 'News', 'Photos', 'Parade', 'ParadeRegistration', 'Membership', 'Tickets', 'App', 'Contact')");
                    table.CheckConstraint("CK_WebsiteSettings_singleton", "[id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "CommitteeMember",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    committee_id = table.Column<int>(type: "int", nullable: false),
                    member_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    function = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    photo_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommitteeMember", x => x.id);
                    table.ForeignKey(
                        name: "FK_CommitteeMember_Committee_committee_id",
                        column: x => x.committee_id,
                        principalSchema: "content",
                        principalTable: "Committee",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommitteeMember_Member_member_id",
                        column: x => x.member_id,
                        principalSchema: "membership",
                        principalTable: "Member",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                schema: "content",
                table: "Committee",
                columns: new[] { "id", "name", "slug", "sort_order" },
                values: new object[,]
                {
                    { 1, "Bestuur", "bestuur", 10 },
                    { 2, "Raad van Elf", "raad-van-elf", 20 },
                    { 3, "Convent", "convent", 30 },
                    { 4, "Leiding dansgarde", "leiding-dansgarde", 40 }
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "Permission",
                columns: new[] { "id", "category", "code", "description" },
                values: new object[] { 43, "Content", "website.manage", "Website beheren: homepage, pagina's, kader, prinsen en onderscheidingen" });

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 10,
                column: "description",
                value: "Nieuws, agenda, foto's en de website");

            migrationBuilder.InsertData(
                schema: "content",
                table: "WebsiteSettings",
                columns: new[] { "id", "created_at", "created_by", "facebook_page_url", "hero_eyebrow", "hero_image_blob_path", "hero_primary_label", "hero_primary_link", "hero_secondary_label", "hero_secondary_link", "hero_subtitle", "hero_title", "instagram_url", "show_youth_princes", "updated_at", "updated_by" },
                values: new object[] { 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "https://www.facebook.com/vrolijkedrammers", "CARNAVAL · LOIL", null, "Bekijk de agenda", "Agenda", "Word lid", "Membership", "Pronkzitting, optocht, dansgarde en vier dagen feest. Volg alles van De Vrolijke Drammers hier en in onze app.", "Alaaf! Het feest komt eraan.", "https://www.instagram.com/vrolijkedrammers", false, null, null });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "RolePermission",
                columns: new[] { "permission_id", "role_id" },
                values: new object[,]
                {
                    { 43, 10 },
                    { 43, 11 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_News_slug",
                schema: "content",
                table: "News",
                column: "slug",
                unique: true,
                filter: "[slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Award_slug",
                schema: "content",
                table: "Award",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Award_year_type",
                schema: "content",
                table: "Award",
                columns: new[] { "year", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_Committee_slug",
                schema: "content",
                table: "Committee",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeMember_committee_id_sort_order",
                schema: "content",
                table: "CommitteeMember",
                columns: new[] { "committee_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_CommitteeMember_member_id",
                schema: "content",
                table: "CommitteeMember",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_Prince_kind_year",
                schema: "content",
                table: "Prince",
                columns: new[] { "kind", "year" });

            migrationBuilder.CreateIndex(
                name: "IX_WebsitePage_slug",
                schema: "content",
                table: "WebsitePage",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Award",
                schema: "content");

            migrationBuilder.DropTable(
                name: "CommitteeMember",
                schema: "content");

            migrationBuilder.DropTable(
                name: "Prince",
                schema: "content");

            migrationBuilder.DropTable(
                name: "WebsitePage",
                schema: "content");

            migrationBuilder.DropTable(
                name: "WebsiteSettings",
                schema: "content");

            migrationBuilder.DropTable(
                name: "Committee",
                schema: "content");

            migrationBuilder.DropIndex(
                name: "IX_News_slug",
                schema: "content",
                table: "News");

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 43, 10 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "RolePermission",
                keyColumns: new[] { "permission_id", "role_id" },
                keyValues: new object[] { 43, 11 });

            migrationBuilder.DeleteData(
                schema: "identity",
                table: "Permission",
                keyColumn: "id",
                keyValue: 43);

            migrationBuilder.DropColumn(
                name: "show_on_website",
                schema: "content",
                table: "News");

            migrationBuilder.DropColumn(
                name: "slug",
                schema: "content",
                table: "News");

            migrationBuilder.DropColumn(
                name: "website_body",
                schema: "content",
                table: "News");

            migrationBuilder.UpdateData(
                schema: "identity",
                table: "Role",
                keyColumn: "id",
                keyValue: 10,
                column: "description",
                value: "Nieuws, agenda en foto's");
        }
    }
}
