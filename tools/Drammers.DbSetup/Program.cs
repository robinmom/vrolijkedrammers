using Drammers.Infrastructure;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Setup;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Databasestappen van de deploy-pipeline. Aanmelden gebeurt met de pipeline-identiteit (lid van de SQL-beheergroep)
// via Azure CLI/DefaultAzureCredential.
//   Drammers.DbSetup <server> <database> migrate <script.sql>
//       Voert het idempotente EF-migratiescript uit; bij een fout stopt de deploy (exitcode ≠ 0).
//   Drammers.DbSetup <server> <database> ensure-user <gebruikersnaam> <client-id> [rol ...]
//       Maakt de managed identity aan als databasegebruiker en maakt haar lid van de rollen (idempotent).
//   Drammers.DbSetup <server> <database> bootstrap-admin <oid> <e-mail> <naam>
//       Koppelt een bestaand Entra-account als eerste beheerder (rol beheerder-it; idempotent, geaudit). Nodig omdat
//       beheerders anders alleen door een andere beheerder kunnen worden aangemaakt (fase 3, ADR-014 bron Manual).
if (args.Length < 4)
{
    Console.Error.WriteLine("Gebruik: Drammers.DbSetup <server> <database> migrate <script.sql>");
    Console.Error.WriteLine("         Drammers.DbSetup <server> <database> ensure-user <naam> <client-id> [rol ...]");
    Console.Error.WriteLine("         Drammers.DbSetup <server> <database> bootstrap-admin <oid> <e-mail> <naam>");
    return 2;
}

var (server, database, command) = (args[0], args[1], args[2]);
var connectionString = $"Server=tcp:{server},1433;Database={database};Authentication=Active Directory Default;Encrypt=True;Connect Timeout=90";

switch (command)
{
    case "migrate":
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            var script = await File.ReadAllTextAsync(args[3]);
            var batches = await SqlScriptRunner.RunAsync(connection, script, CancellationToken.None);
            Console.WriteLine($"Migratiescript uitgevoerd ({batches} batches).");
            return 0;
        }

    case "ensure-user" when args.Length >= 5 && Guid.TryParse(args[4], out var clientId):
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            var roles = args.Skip(5).ToArray();
            var created = await DatabaseUserProvisioner.EnsureEntraUserAsync(connection, args[3], clientId, roles, CancellationToken.None);
            Console.WriteLine($"Databasegebruiker '{args[3]}' {(created ? "aangemaakt" : "bestaat al")}; rollen: {string.Join(", ", roles)}.");
            return 0;
        }

    case "bootstrap-admin" when args.Length == 6:
        return await BootstrapAdminAsync(connectionString, objectId: args[3], email: args[4], displayName: args[5]);

    default:
        Console.Error.WriteLine($"Onbekend of onvolledig commando: {command}");
        return 2;
}

static async Task<int> BootstrapAdminAsync(string connectionString, string objectId, string email, string displayName)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IClock, SystemClock>();
    services.AddDrammersDatabase(connectionString);
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();

    var roleId = await db.Roles.Where(r => r.Code == "beheerder-it").Select(r => r.Id).SingleAsync();
    var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.ExternalObjectId == objectId);
    if (user?.Roles.Any(r => r.RoleId == roleId) == true)
    {
        Console.WriteLine("Beheerder bestaat al; niets gewijzigd.");
        return 0;
    }

    await using var transaction = await db.Database.BeginTransactionAsync();
    if (user is null)
    {
        user = new User
        {
            Id = IdGenerator.NewId(),
            ExternalObjectId = objectId,
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = displayName,
            AccountStatus = AccountStatus.Active,
        };
        db.Users.Add(user);
    }

    user.Roles.Add(new UserRole { UserId = user.Id, RoleId = roleId, AssignedAt = DateTime.UtcNow });
    user.PermissionsVersion++;
    await db.SaveChangesAsync();
    await scope.ServiceProvider.GetRequiredService<IAuditLogger>().WriteAsync(
        new AuditEntry("user.bootstrapped-admin", "User", user.Id.ToString(), null, "{\"role\":\"beheerder-it\",\"source\":\"pipeline\"}"));
    await transaction.CommitAsync();
    Console.WriteLine($"Eerste beheerder gekoppeld (gebruiker {user.Id}).");
    return 0;
}
