using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParadeRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "parade");

            migrationBuilder.CreateTable(
                name: "Parade",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    parade_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    start_location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    route_description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    route_length_km = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    registration_opens_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    registration_closes_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    edit_deadline_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    subject_required = table.Column<bool>(type: "bit", nullable: false),
                    default_spacing_meters = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    max_documents_per_registration = table.Column<int>(type: "int", nullable: false),
                    max_document_size_mb = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    composition_version = table.Column<int>(type: "int", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parade", x => x.id);
                    table.CheckConstraint("CK_Parade_registration_period", "[registration_closes_at] > [registration_opens_at]");
                    table.CheckConstraint("CK_Parade_status", "[status] IN ('Planned', 'RegistrationOpen', 'RegistrationClosed', 'Composing', 'Final', 'Completed')");
                    table.ForeignKey(
                        name: "FK_Parade_CarnivalYear_carnival_year_id",
                        column: x => x.carnival_year_id,
                        principalSchema: "content",
                        principalTable: "CarnivalYear",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParadeCategory",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    age_group = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    minimum_participants = table.Column<int>(type: "int", nullable: true),
                    maximum_participants = table.Column<int>(type: "int", nullable: true),
                    participant_count_basis = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    validation_mode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    has_vehicle = table.Column<bool>(type: "bit", nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeCategory", x => x.id);
                    table.CheckConstraint("CK_ParadeCategory_age_group", "[age_group] IN ('Adult', 'Youth')");
                    table.CheckConstraint("CK_ParadeCategory_participant_count_basis", "[participant_count_basis] IN ('Total', 'ChildrenOnly', 'AdultsOnly')");
                    table.CheckConstraint("CK_ParadeCategory_range", "[minimum_participants] IS NULL OR [maximum_participants] IS NULL OR [minimum_participants] <= [maximum_participants]");
                    table.CheckConstraint("CK_ParadeCategory_type", "[type] IN ('TowedFloat', 'SelfPropelled', 'TowedOrSelfPropelled', 'WalkingGroupLarge', 'WalkingGroupSmall', 'IndividualDuo')");
                    table.CheckConstraint("CK_ParadeCategory_validation_mode", "[validation_mode] IN ('Block', 'Warn', 'None')");
                    table.ForeignKey(
                        name: "FK_ParadeCategory_Parade_parade_id",
                        column: x => x.parade_id,
                        principalSchema: "parade",
                        principalTable: "Parade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParadeNumberSequence",
                schema: "parade",
                columns: table => new
                {
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    last_registration_number = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeNumberSequence", x => x.parade_id);
                    table.ForeignKey(
                        name: "FK_ParadeNumberSequence_Parade_parade_id",
                        column: x => x.parade_id,
                        principalSchema: "parade",
                        principalTable: "Parade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParadeStatusEditPolicy",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    actor_scope = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    editable_fields = table.Column<string>(type: "varchar(1000)", unicode: false, maxLength: 1000, nullable: false),
                    can_withdraw = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeStatusEditPolicy", x => x.id);
                    table.CheckConstraint("CK_ParadeStatusEditPolicy_actor_scope", "[actor_scope] IN ('Owner', 'Committee', 'SpecialAdmin')");
                    table.CheckConstraint("CK_ParadeStatusEditPolicy_status", "[status] IN ('Draft', 'Submitted', 'UnderReview', 'AdditionalInformationRequired', 'Approved', 'Rejected', 'Withdrawn', 'StartNumberAssigned', 'Final')");
                    table.ForeignKey(
                        name: "FK_ParadeStatusEditPolicy_Parade_parade_id",
                        column: x => x.parade_id,
                        principalSchema: "parade",
                        principalTable: "Parade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParadeRegistration",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parade_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carnival_year_id = table.Column<int>(type: "int", nullable: false),
                    registration_number = table.Column<int>(type: "int", nullable: true),
                    start_number = table.Column<int>(type: "int", nullable: true),
                    parade_order = table.Column<int>(type: "int", nullable: true),
                    group_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    contact_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    contact_phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    contact_email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    category_id = table.Column<int>(type: "int", nullable: true),
                    subject = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    subject_description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    children_count = table.Column<int>(type: "int", nullable: false),
                    adult_count = table.Column<int>(type: "int", nullable: false),
                    build_address_street = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    build_address_house_number = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: true),
                    build_address_addition = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    build_address_postal_code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    build_address_city = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    build_address_country = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    jury_inspection_same_as_build_address = table.Column<bool>(type: "bit", nullable: false),
                    jury_inspection_address_street = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    jury_inspection_address_house_number = table.Column<string>(type: "varchar(5)", unicode: false, maxLength: 5, nullable: true),
                    jury_inspection_address_addition = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    jury_inspection_address_postal_code = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true),
                    jury_inspection_address_city = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    jury_inspection_address_country = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    estimated_length_meters = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    measured_length_meters = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    spacing_after_meters = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    additional_information = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    validation_warnings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    withdrawn_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contact_email_verified_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    source = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    deadline_reminder_sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    total_participants = table.Column<int>(type: "int", nullable: false, computedColumnSql: "[children_count] + [adult_count]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeRegistration", x => x.id);
                    table.CheckConstraint("CK_ParadeRegistration_counts", "[children_count] >= 0 AND [adult_count] >= 0");
                    table.CheckConstraint("CK_ParadeRegistration_estimated_length", "[estimated_length_meters] IS NULL OR ([estimated_length_meters] > 0 AND [estimated_length_meters] <= 100)");
                    table.CheckConstraint("CK_ParadeRegistration_measured_length", "[measured_length_meters] IS NULL OR ([measured_length_meters] > 0 AND [measured_length_meters] <= 100)");
                    table.CheckConstraint("CK_ParadeRegistration_number_draft", "[status] <> 'Draft' OR [registration_number] IS NULL");
                    table.CheckConstraint("CK_ParadeRegistration_number_submitted", "[status] = 'Draft' OR [registration_number] IS NOT NULL");
                    table.CheckConstraint("CK_ParadeRegistration_source", "[source] IN ('App', 'WebForm', 'Portal')");
                    table.CheckConstraint("CK_ParadeRegistration_start_number", "[start_number] IS NULL OR [start_number] > 0");
                    table.CheckConstraint("CK_ParadeRegistration_status", "[status] IN ('Draft', 'Submitted', 'UnderReview', 'AdditionalInformationRequired', 'Approved', 'Rejected', 'Withdrawn', 'StartNumberAssigned', 'Final')");
                    table.ForeignKey(
                        name: "FK_ParadeRegistration_ParadeCategory_category_id",
                        column: x => x.category_id,
                        principalSchema: "parade",
                        principalTable: "ParadeCategory",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ParadeRegistration_Parade_parade_id",
                        column: x => x.parade_id,
                        principalSchema: "parade",
                        principalTable: "Parade",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ParadeDocument",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    document_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    blob_path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeDocument", x => x.id);
                    table.CheckConstraint("CK_ParadeDocument_document_type", "[document_type] IN ('Insurance', 'VehicleInspection', 'Drawing', 'Other')");
                    table.ForeignKey(
                        name: "FK_ParadeDocument_ParadeRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "parade",
                        principalTable: "ParadeRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParadeRegistrationHistory",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    field_name = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    field_label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    old_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    changed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    changed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    change_source = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeRegistrationHistory", x => x.id);
                    table.CheckConstraint("CK_ParadeRegistrationHistory_change_source", "[change_source] IN ('App', 'WebForm', 'Portal')");
                    table.ForeignKey(
                        name: "FK_ParadeRegistrationHistory_ParadeRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "parade",
                        principalTable: "ParadeRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParadeRegistrationManager",
                schema: "parade",
                columns: table => new
                {
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    added_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeRegistrationManager", x => new { x.registration_id, x.user_id });
                    table.CheckConstraint("CK_ParadeRegistrationManager_role", "[role] IN ('Owner', 'CoManager')");
                    table.ForeignKey(
                        name: "FK_ParadeRegistrationManager_ParadeRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "parade",
                        principalTable: "ParadeRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParadeRegistrationManager_User_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "User",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParadeStatusHistory",
                schema: "parade",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    registration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    to_status = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    occurred_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParadeStatusHistory", x => x.id);
                    table.CheckConstraint("CK_ParadeStatusHistory_from_status", "[from_status] IN ('Draft', 'Submitted', 'UnderReview', 'AdditionalInformationRequired', 'Approved', 'Rejected', 'Withdrawn', 'StartNumberAssigned', 'Final')");
                    table.CheckConstraint("CK_ParadeStatusHistory_to_status", "[to_status] IN ('Draft', 'Submitted', 'UnderReview', 'AdditionalInformationRequired', 'Approved', 'Rejected', 'Withdrawn', 'StartNumberAssigned', 'Final')");
                    table.ForeignKey(
                        name: "FK_ParadeStatusHistory_ParadeRegistration_registration_id",
                        column: x => x.registration_id,
                        principalSchema: "parade",
                        principalTable: "ParadeRegistration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "parade",
                table: "ParadeCategory",
                columns: new[] { "id", "active", "age_group", "code", "has_vehicle", "maximum_participants", "minimum_participants", "name", "parade_id", "participant_count_basis", "sort_order", "type", "validation_mode" },
                values: new object[,]
                {
                    { 1, true, "Adult", "ADULT_TOWED", true, null, 1, "Volwassenen Getrokken wagens", null, "AdultsOnly", 10, "TowedFloat", "Warn" },
                    { 2, true, "Adult", "ADULT_SELF", true, null, 1, "Volwassenen Zelfrijdende voertuigen", null, "AdultsOnly", 20, "SelfPropelled", "Warn" },
                    { 3, true, "Adult", "ADULT_WALK_L", false, null, 10, "Volwassenen Loopgroepen groot (10+)", null, "AdultsOnly", 30, "WalkingGroupLarge", "Block" },
                    { 4, true, "Adult", "ADULT_WALK_S", false, 9, 3, "Volwassenen Loopgroepen klein (3-9)", null, "AdultsOnly", 40, "WalkingGroupSmall", "Block" },
                    { 5, true, "Adult", "ADULT_INDIV", false, 2, 1, "Volwassenen Individueel of duo (1-2)", null, "AdultsOnly", 50, "IndividualDuo", "Block" },
                    { 6, true, "Youth", "YOUTH_FLOAT", true, null, 1, "Jeugd Getrokken en zelfrijdende wagens", null, "ChildrenOnly", 60, "TowedOrSelfPropelled", "Warn" },
                    { 7, true, "Youth", "YOUTH_WALK_L", false, null, 10, "Jeugd Loopgroepen groot (10+)", null, "ChildrenOnly", 70, "WalkingGroupLarge", "Block" },
                    { 8, true, "Youth", "YOUTH_WALK_S", false, 9, 3, "Jeugd Loopgroepen klein (3-9)", null, "ChildrenOnly", 80, "WalkingGroupSmall", "Block" },
                    { 9, true, "Youth", "YOUTH_INDIV", false, 2, 1, "Jeugd Individueel of duo (1-2)", null, "ChildrenOnly", 90, "IndividualDuo", "Block" }
                });

            migrationBuilder.InsertData(
                schema: "parade",
                table: "ParadeStatusEditPolicy",
                columns: new[] { "id", "actor_scope", "can_withdraw", "editable_fields", "parade_id", "status" },
                values: new object[,]
                {
                    { 1, "Owner", false, "*", null, "Draft" },
                    { 2, "Owner", true, "contactName,contactPhone,contactEmail,childrenCount,adultCount,additionalInformation,documents,estimatedLengthMeters", null, "Submitted" },
                    { 3, "Owner", true, "contactName,contactPhone,contactEmail,additionalInformation,documents", null, "UnderReview" },
                    { 4, "Owner", true, "contactName,contactPhone,contactEmail,childrenCount,adultCount,additionalInformation,documents,estimatedLengthMeters", null, "AdditionalInformationRequired" },
                    { 5, "Owner", true, "contactName,contactPhone,contactEmail,documents", null, "Approved" },
                    { 6, "Owner", true, "contactName,contactPhone,contactEmail", null, "StartNumberAssigned" },
                    { 7, "Owner", false, "", null, "Final" },
                    { 8, "Owner", false, "", null, "Rejected" },
                    { 9, "Owner", false, "", null, "Withdrawn" },
                    { 10, "Committee", true, "*", null, "Draft" },
                    { 11, "SpecialAdmin", true, "*", null, "Draft" },
                    { 12, "Committee", true, "*", null, "Submitted" },
                    { 13, "SpecialAdmin", true, "*", null, "Submitted" },
                    { 14, "Committee", true, "*", null, "UnderReview" },
                    { 15, "SpecialAdmin", true, "*", null, "UnderReview" },
                    { 16, "Committee", true, "*", null, "AdditionalInformationRequired" },
                    { 17, "SpecialAdmin", true, "*", null, "AdditionalInformationRequired" },
                    { 18, "Committee", true, "*", null, "Approved" },
                    { 19, "SpecialAdmin", true, "*", null, "Approved" },
                    { 20, "Committee", true, "*", null, "Rejected" },
                    { 21, "SpecialAdmin", true, "*", null, "Rejected" },
                    { 22, "Committee", true, "*", null, "Withdrawn" },
                    { 23, "SpecialAdmin", true, "*", null, "Withdrawn" },
                    { 24, "Committee", true, "*", null, "StartNumberAssigned" },
                    { 25, "SpecialAdmin", true, "*", null, "StartNumberAssigned" },
                    { 26, "Committee", false, "", null, "Final" },
                    { 27, "SpecialAdmin", true, "*", null, "Final" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Parade_carnival_year_id",
                schema: "parade",
                table: "Parade",
                column: "carnival_year_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParadeCategory_code",
                schema: "parade",
                table: "ParadeCategory",
                column: "code",
                unique: true,
                filter: "[parade_id] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeCategory_parade_id_code",
                schema: "parade",
                table: "ParadeCategory",
                columns: new[] { "parade_id", "code" },
                unique: true,
                filter: "[parade_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeDocument_registration_id",
                schema: "parade",
                table: "ParadeDocument",
                column: "registration_id");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistration_category_id",
                schema: "parade",
                table: "ParadeRegistration",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistration_parade_id_registration_number",
                schema: "parade",
                table: "ParadeRegistration",
                columns: new[] { "parade_id", "registration_number" },
                unique: true,
                filter: "[registration_number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistration_parade_id_start_number",
                schema: "parade",
                table: "ParadeRegistration",
                columns: new[] { "parade_id", "start_number" },
                unique: true,
                filter: "[start_number] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistration_parade_id_status",
                schema: "parade",
                table: "ParadeRegistration",
                columns: new[] { "parade_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistrationHistory_registration_id_changed_at",
                schema: "parade",
                table: "ParadeRegistrationHistory",
                columns: new[] { "registration_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_ParadeRegistrationManager_user_id",
                schema: "parade",
                table: "ParadeRegistrationManager",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeStatusEditPolicy_parade_id_status_actor_scope",
                schema: "parade",
                table: "ParadeStatusEditPolicy",
                columns: new[] { "parade_id", "status", "actor_scope" },
                unique: true,
                filter: "[parade_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParadeStatusHistory_registration_id",
                schema: "parade",
                table: "ParadeStatusHistory",
                column: "registration_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ParadeDocument",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "ParadeNumberSequence",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "ParadeRegistrationHistory",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "ParadeRegistrationManager",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "ParadeStatusEditPolicy",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "ParadeStatusHistory",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "ParadeRegistration",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "ParadeCategory",
                schema: "parade");

            migrationBuilder.DropTable(
                name: "Parade",
                schema: "parade");
        }
    }
}
