using Microsoft.Data.SqlClient;

// Maakt de managed identity van de API aan als databasegebruiker (idempotent). De pipeline draait dit na elke
// infra-uitrol, zodat een opnieuw opgebouwde omgeving zonder handmatige stap werkt. Aanmelden gebeurt met de
// pipeline-identiteit (lid van de SQL-beheergroep) via Azure CLI/DefaultAzureCredential.
// Gebruik: Drammers.DbSetup <sql-server-fqdn> <database> <gebruikersnaam> <client-id van de managed identity>
// Rechten volgen in fase 2 (migraties); SELECT 1 voor /health/ready heeft alleen CONNECT nodig.
if (args.Length != 4 || !Guid.TryParse(args[3], out var clientId))
{
    Console.Error.WriteLine("Gebruik: Drammers.DbSetup <server> <database> <gebruikersnaam> <client-id>");
    return 2;
}

var (server, database, userName) = (args[0], args[1], args[2]);
var connectionString = $"Server=tcp:{server},1433;Database={database};Authentication=Active Directory Default;Encrypt=True;Connect Timeout=90";

// WITH SID + TYPE = E: geen Graph-opzoeking door de SQL-server nodig (die heeft geen Directory Readers-rol).
const string Sql = """
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
    BEGIN
        DECLARE @sid NVARCHAR(100) = CONVERT(NVARCHAR(100), CONVERT(VARBINARY(16), @clientId), 1);
        EXEC (N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + @sid + N', TYPE = E');
        SELECT 1;
    END
    ELSE SELECT 0;
    """;

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
await using var command = new SqlCommand(Sql, connection);
command.Parameters.AddWithValue("@name", userName);
command.Parameters.AddWithValue("@clientId", clientId);
var created = (int)(await command.ExecuteScalarAsync() ?? 0) == 1;
Console.WriteLine(created ? $"Databasegebruiker '{userName}' aangemaakt." : $"Databasegebruiker '{userName}' bestaat al.");
return 0;
