using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventCategory",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventCategory", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "News",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    image_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    author_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    publish_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    expire_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    visibility = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_News", x => x.id);
                    table.CheckConstraint("CK_News_status", "[status] IN ('Draft', 'Scheduled', 'Published', 'Archived')");
                    table.CheckConstraint("CK_News_visibility", "[visibility] IN ('Public', 'Members', 'Restricted')");
                });

            migrationBuilder.CreateTable(
                name: "PhotoAlbum",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: true),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    album_date = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    visibility = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    publish_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    cover_photo_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAlbum", x => x.id);
                    table.CheckConstraint("CK_PhotoAlbum_status", "[status] IN ('Draft', 'Scheduled', 'Published', 'Archived')");
                    table.CheckConstraint("CK_PhotoAlbum_visibility", "[visibility] IN ('Public', 'Members', 'Restricted')");
                });

            migrationBuilder.CreateTable(
                name: "Event",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    category_id = table.Column<int>(type: "int", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    start_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    end_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    all_day = table.Column<bool>(type: "bit", nullable: false),
                    location_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    location_address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    image_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    visibility = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    publish_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    is_highlight = table.Column<bool>(type: "bit", nullable: false),
                    badge_text = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Event", x => x.id);
                    table.CheckConstraint("CK_Event_status", "[status] IN ('Draft', 'Scheduled', 'Published', 'Archived')");
                    table.CheckConstraint("CK_Event_visibility", "[visibility] IN ('Public', 'Members', 'Restricted')");
                    table.ForeignKey(
                        name: "FK_Event_CarnivalYear_carnival_year_id",
                        column: x => x.carnival_year_id,
                        principalSchema: "content",
                        principalTable: "CarnivalYear",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Event_EventCategory_category_id",
                        column: x => x.category_id,
                        principalSchema: "content",
                        principalTable: "EventCategory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NewsAudience",
                schema: "content",
                columns: table => new
                {
                    news_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    audience_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    audience_ref = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsAudience", x => new { x.news_id, x.audience_type, x.audience_ref });
                    table.CheckConstraint("CK_NewsAudience_audience_type", "[audience_type] IN ('Role', 'Group', 'Member')");
                    table.ForeignKey(
                        name: "FK_NewsAudience_News_news_id",
                        column: x => x.news_id,
                        principalSchema: "content",
                        principalTable: "News",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Photo",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    album_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    original_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    display_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    thumbnail_blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    processing_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    width = table.Column<int>(type: "int", nullable: true),
                    height = table.Column<int>(type: "int", nullable: true),
                    taken_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    photographer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    hidden = table.Column<bool>(type: "bit", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photo", x => x.id);
                    table.CheckConstraint("CK_Photo_processing_status", "[processing_status] IN ('Pending', 'Ready', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_Photo_PhotoAlbum_album_id",
                        column: x => x.album_id,
                        principalSchema: "content",
                        principalTable: "PhotoAlbum",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAlbumAudience",
                schema: "content",
                columns: table => new
                {
                    album_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    audience_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    audience_ref = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAlbumAudience", x => new { x.album_id, x.audience_type, x.audience_ref });
                    table.CheckConstraint("CK_PhotoAlbumAudience_audience_type", "[audience_type] IN ('Role', 'Group', 'Member')");
                    table.ForeignKey(
                        name: "FK_PhotoAlbumAudience_PhotoAlbum_album_id",
                        column: x => x.album_id,
                        principalSchema: "content",
                        principalTable: "PhotoAlbum",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventAttachment",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAttachment", x => x.id);
                    table.ForeignKey(
                        name: "FK_EventAttachment_Event_event_id",
                        column: x => x.event_id,
                        principalSchema: "content",
                        principalTable: "Event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventAudience",
                schema: "content",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    audience_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    audience_ref = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAudience", x => new { x.event_id, x.audience_type, x.audience_ref });
                    table.CheckConstraint("CK_EventAudience_audience_type", "[audience_type] IN ('Role', 'Group', 'Member')");
                    table.ForeignKey(
                        name: "FK_EventAudience_Event_event_id",
                        column: x => x.event_id,
                        principalSchema: "content",
                        principalTable: "Event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "content",
                table: "EventCategory",
                columns: new[] { "id", "code", "name", "sort_order" },
                values: new object[,]
                {
                    { 1, "carnaval", "Carnaval", 10 },
                    { 2, "jeugd", "Jeugd", 20 },
                    { 3, "vereniging", "Vereniging", 30 },
                    { 4, "kader", "Kader", 40 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Event_carnival_year_id",
                schema: "content",
                table: "Event",
                column: "carnival_year_id");

            migrationBuilder.CreateIndex(
                name: "IX_Event_category_id",
                schema: "content",
                table: "Event",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_Event_status_start_at",
                schema: "content",
                table: "Event",
                columns: new[] { "status", "start_at" });

            migrationBuilder.CreateIndex(
                name: "IX_EventAttachment_event_id",
                schema: "content",
                table: "EventAttachment",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_EventCategory_code",
                schema: "content",
                table: "EventCategory",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_News_status_publish_at",
                schema: "content",
                table: "News",
                columns: new[] { "status", "publish_at" });

            migrationBuilder.CreateIndex(
                name: "IX_Photo_album_id_sort_order",
                schema: "content",
                table: "Photo",
                columns: new[] { "album_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAlbum_status_album_date",
                schema: "content",
                table: "PhotoAlbum",
                columns: new[] { "status", "album_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventAttachment",
                schema: "content");

            migrationBuilder.DropTable(
                name: "EventAudience",
                schema: "content");

            migrationBuilder.DropTable(
                name: "NewsAudience",
                schema: "content");

            migrationBuilder.DropTable(
                name: "Photo",
                schema: "content");

            migrationBuilder.DropTable(
                name: "PhotoAlbumAudience",
                schema: "content");

            migrationBuilder.DropTable(
                name: "Event",
                schema: "content");

            migrationBuilder.DropTable(
                name: "News",
                schema: "content");

            migrationBuilder.DropTable(
                name: "PhotoAlbum",
                schema: "content");

            migrationBuilder.DropTable(
                name: "EventCategory",
                schema: "content");
        }
    }
}
