using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drammers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Schema's voor modules die nog geen tabellen hebben, zodat rechten per schema vanaf nu gelden (docs/04 §2).
            foreach (var schema in new[] { "identity", "membership", "ticketing", "payments", "parade", "import", "reporting" })
            {
                migrationBuilder.EnsureSchema(schema);
            }

            // Least-privilege rollen (fase 2, docs/08 §4). De API-identiteit wordt lid van app_runtime (tools/Drammers.DbSetup).
            migrationBuilder.Sql("CREATE ROLE [app_runtime];");
            migrationBuilder.Sql("CREATE ROLE [app_migrator];");
            migrationBuilder.Sql("CREATE ROLE [app_reporting];");

            foreach (var schema in new[] { "identity", "membership", "content", "notification", "ticketing", "payments", "parade", "import", "config" })
            {
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[{schema}] TO [app_runtime];");
            }

            // Auditlog is append-only voor de runtime: lezen en toevoegen, nooit wijzigen of verwijderen.
            // Geen DENY CONTROL: dat zou ook SELECT en INSERT weigeren.
            migrationBuilder.Sql("GRANT SELECT, INSERT ON SCHEMA::[audit] TO [app_runtime];");
            migrationBuilder.Sql("DENY UPDATE, DELETE, ALTER, TAKE OWNERSHIP ON SCHEMA::[audit] TO [app_runtime];");

            migrationBuilder.Sql("ALTER ROLE [db_ddladmin] ADD MEMBER [app_migrator];");
            migrationBuilder.Sql("ALTER ROLE [db_datareader] ADD MEMBER [app_migrator];");
            migrationBuilder.Sql("ALTER ROLE [db_datawriter] ADD MEMBER [app_migrator];");

            migrationBuilder.Sql("GRANT SELECT ON SCHEMA::[reporting] TO [app_reporting];");

            // Data Discovery & Classification: de auditlog kan persoonsgegevens bevatten (oude/nieuwe waarden).
            migrationBuilder.Sql(
                "ADD SENSITIVITY CLASSIFICATION TO [audit].[AuditLog].[old_values], [audit].[AuditLog].[new_values] " +
                "WITH (LABEL = 'Confidential - GDPR', INFORMATION_TYPE = 'Other', RANK = MEDIUM);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SENSITIVITY CLASSIFICATION FROM [audit].[AuditLog].[old_values], [audit].[AuditLog].[new_values];");
            migrationBuilder.Sql("ALTER ROLE [db_ddladmin] DROP MEMBER [app_migrator];");
            migrationBuilder.Sql("ALTER ROLE [db_datareader] DROP MEMBER [app_migrator];");
            migrationBuilder.Sql("ALTER ROLE [db_datawriter] DROP MEMBER [app_migrator];");
            migrationBuilder.Sql("DROP ROLE [app_reporting];");
            migrationBuilder.Sql("DROP ROLE [app_migrator];");
            migrationBuilder.Sql("DROP ROLE [app_runtime];");

            foreach (var schema in new[] { "identity", "membership", "ticketing", "payments", "parade", "import", "reporting" })
            {
                migrationBuilder.Sql($"DROP SCHEMA [{schema}];");
            }
        }
    }
}
