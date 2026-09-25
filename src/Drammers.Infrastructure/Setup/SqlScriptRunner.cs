using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Drammers.Infrastructure.Setup;

/// <summary>Voert een SQL-script uit dat met <c>GO</c> in batches is verdeeld (zoals het idempotente EF-migratiescript).</summary>
public static partial class SqlScriptRunner
{
    public static async Task<int> RunAsync(SqlConnection connection, string script, CancellationToken cancellationToken)
    {
        var batches = BatchSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
        foreach (var batch in batches)
        {
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 600 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return batches.Count;
    }

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();
}
