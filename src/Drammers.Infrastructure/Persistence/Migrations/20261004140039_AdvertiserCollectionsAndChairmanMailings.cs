using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvertiserCollectionsAndChairmanMailings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "company",
                schema: "notification",
                table: "MailingRecipient",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "all_advertisers",
                schema: "notification",
                table: "MailingList",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "sender",
                schema: "notification",
                table: "Mailing",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "Secretary");

            migrationBuilder.AlterColumn<Guid>(
                name: "member_id",
                schema: "membership",
                table: "CollectionRunLine",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "advertiser_id",
                schema: "membership",
                table: "CollectionRunLine",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "campaign_year",
                schema: "membership",
                table: "CollectionRun",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "membership",
                table: "CollectionRun",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: false,
                defaultValue: "Contribution");

            // Standaardgroep voor de informatiebrief: alle actieve adverteerders met een e-mailadres (fase 27c).
            migrationBuilder.InsertData(
                schema: "notification",
                table: "MailingList",
                columns: new[] { "id", "name", "description", "all_members", "all_advertisers", "created_at" },
                values: new object[] { new Guid("0193a000-0000-7000-8000-000000000002"), "Adverteerders", "Alle actieve adverteerders met een e-mailadres.", false, true, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Mailing_sender",
                schema: "notification",
                table: "Mailing",
                sql: "[sender] IN ('Secretary', 'Chairman')");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRunLine_advertiser_id",
                schema: "membership",
                table: "CollectionRunLine",
                column: "advertiser_id");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRun_kind_campaign_year",
                schema: "membership",
                table: "CollectionRun",
                columns: new[] { "kind", "campaign_year" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CollectionRun_kind",
                schema: "membership",
                table: "CollectionRun",
                sql: "[kind] IN ('Contribution', 'Advertisers')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Mailing_sender",
                schema: "notification",
                table: "Mailing");

            migrationBuilder.DropIndex(
                name: "IX_CollectionRunLine_advertiser_id",
                schema: "membership",
                table: "CollectionRunLine");

            migrationBuilder.DropIndex(
                name: "IX_CollectionRun_kind_campaign_year",
                schema: "membership",
                table: "CollectionRun");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CollectionRun_kind",
                schema: "membership",
                table: "CollectionRun");

            migrationBuilder.DropColumn(
                name: "company",
                schema: "notification",
                table: "MailingRecipient");

            migrationBuilder.DropColumn(
                name: "all_advertisers",
                schema: "notification",
                table: "MailingList");

            migrationBuilder.DropColumn(
                name: "sender",
                schema: "notification",
                table: "Mailing");

            migrationBuilder.DropColumn(
                name: "advertiser_id",
                schema: "membership",
                table: "CollectionRunLine");

            migrationBuilder.DropColumn(
                name: "campaign_year",
                schema: "membership",
                table: "CollectionRun");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "membership",
                table: "CollectionRun");

            migrationBuilder.AlterColumn<Guid>(
                name: "member_id",
                schema: "membership",
                table: "CollectionRunLine",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
