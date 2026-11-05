using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContributieEersteJaar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MembershipApplication_status",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.AddColumn<string>(
                name: "mollie_payment_id",
                schema: "membership",
                table: "MembershipApplication",
                type: "varchar(40)",
                unicode: false,
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at",
                schema: "membership",
                table: "MembershipApplication",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_amount_cents",
                schema: "membership",
                table: "MembershipApplication",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_carnival_year_id",
                schema: "membership",
                table: "MembershipApplication",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_checkout_url",
                schema: "membership",
                table: "MembershipApplication",
                type: "varchar(500)",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_expires_at",
                schema: "membership",
                table: "MembershipApplication",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_requested_at",
                schema: "membership",
                table: "MembershipApplication",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_send_count",
                schema: "membership",
                table: "MembershipApplication",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipApplication_mollie_payment_id",
                schema: "membership",
                table: "MembershipApplication",
                column: "mollie_payment_id",
                unique: true,
                filter: "[mollie_payment_id] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MembershipApplication_status",
                schema: "membership",
                table: "MembershipApplication",
                sql: "[status] IN ('Draft', 'Submitted', 'InReview', 'AwaitingPayment', 'Approved', 'Rejected', 'Provisioning', 'ProvisioningFailed', 'Activated', 'Withdrawn')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MembershipApplication_mollie_payment_id",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MembershipApplication_status",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "mollie_payment_id",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "paid_at",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "payment_amount_cents",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "payment_carnival_year_id",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "payment_checkout_url",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "payment_expires_at",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "payment_requested_at",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.DropColumn(
                name: "payment_send_count",
                schema: "membership",
                table: "MembershipApplication");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MembershipApplication_status",
                schema: "membership",
                table: "MembershipApplication",
                sql: "[status] IN ('Draft', 'Submitted', 'InReview', 'Approved', 'Rejected', 'Provisioning', 'ProvisioningFailed', 'Activated', 'Withdrawn')");
        }
    }
}
