using Drammers.Infrastructure.Setup;
using Microsoft.Data.SqlClient;

// Databasestappen van de deploy-pipeline (fase 2). Aanmelden gebeurt met de pipeline-identiteit (lid van de
// SQL-beheergroep) via Azure CLI/DefaultAzureCredential.
//   Drammers.DbSetup <server> <database> migrate <script.sql>
//       Voert het idempotente EF-migratiescript uit; bij een fout stopt de deploy (exitcode ≠ 0).
//   Drammers.DbSetup <server> <database> ensure-user <gebruikersnaam> <client-id> [rol ...]
//       Maakt de managed identity aan als databasegebruiker en maakt haar lid van de rollen (idempotent).
if (args.Length < 4)
{
    Console.Error.WriteLine("Gebruik: Drammers.DbSetup <server> <database> migrate <script.sql>");
    Console.Error.WriteLine("         Drammers.DbSetup <server> <database> ensure-user <naam> <client-id> [rol ...]");
    return 2;
}

var (server, database, command) = (args[0], args[1], args[2]);
var connectionString = $"Server=tcp:{server},1433;Database={database};Authentication=Active Directory Default;Encrypt=True;Connect Timeout=90";

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

switch (command)
{
    case "migrate":
        var script = await File.ReadAllTextAsync(args[3]);
        var batches = await SqlScriptRunner.RunAsync(connection, script, CancellationToken.None);
        Console.WriteLine($"Migratiescript uitgevoerd ({batches} batches).");
        return 0;

    case "ensure-user" when args.Length >= 5 && Guid.TryParse(args[4], out var clientId):
        var roles = args.Skip(5).ToArray();
        var created = await DatabaseUserProvisioner.EnsureEntraUserAsync(connection, args[3], clientId, roles, CancellationToken.None);
        Console.WriteLine($"Databasegebruiker '{args[3]}' {(created ? "aangemaakt" : "bestaat al")}; rollen: {string.Join(", ", roles)}.");
        return 0;

    default:
        Console.Error.WriteLine($"Onbekend of onvolledig commando: {command}");
        return 2;
}
