using Microsoft.Data.SqlClient;

namespace Drammers.Infrastructure.Setup;

/// <summary>Maakt een Entra-identiteit (managed identity) aan als databasegebruiker en geeft rollen (idempotent).</summary>
public static class DatabaseUserProvisioner
{
    /// <returns><c>true</c> als de gebruiker nieuw is aangemaakt.</returns>
    public static async Task<bool> EnsureEntraUserAsync(
        SqlConnection connection, string userName, Guid clientId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        // WITH SID + TYPE = E: geen Graph-opzoeking door de SQL-server nodig (die heeft geen Directory Readers-rol).
        const string CreateUser = """
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
            BEGIN
                DECLARE @sid NVARCHAR(100) = CONVERT(NVARCHAR(100), CONVERT(VARBINARY(16), @clientId), 1);
                DECLARE @statement NVARCHAR(400) = N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + @sid + N', TYPE = E';
                EXEC sys.sp_executesql @statement;
                SELECT 1;
            END
            ELSE SELECT 0;
            """;
        await using var create = new SqlCommand(CreateUser, connection);
        create.Parameters.AddWithValue("@name", userName);
        create.Parameters.AddWithValue("@clientId", clientId);
        var created = (int)(await create.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;

        const string AddToRole = """
            IF NOT EXISTS (
                SELECT 1 FROM sys.database_role_members m
                JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
                JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
                WHERE r.name = @role AND u.name = @name)
            BEGIN
                DECLARE @statement NVARCHAR(400) = N'ALTER ROLE ' + QUOTENAME(@role) + N' ADD MEMBER ' + QUOTENAME(@name);
                EXEC sys.sp_executesql @statement;
            END
            """;
        foreach (var role in roles)
        {
            await using var add = new SqlCommand(AddToRole, connection);
            add.Parameters.AddWithValue("@name", userName);
            add.Parameters.AddWithValue("@role", role);
            await add.ExecuteNonQueryAsync(cancellationToken);
        }

        return created;
    }
}
